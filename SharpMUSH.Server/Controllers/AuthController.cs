using System.Diagnostics.CodeAnalysis;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Issues One-Time Tokens (OTTs) for web-based MUSH authentication and handles
/// account-level login/registration for the web UI.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
	IMediator mediator,
	IPasswordService passwordService,
	IOttStore ottStore,
	IAccountService accountService,
	IAccountSessionStore accountSessionStore,
	IRoleDerivationService roleDerivation,
	ILogger<AuthController> logger) : ControllerBase
{
	/// <summary>
	/// IJwtService is optional — only registered when Jwt:SigningKey is present in config.
	/// Resolved lazily via RequestServices so the MVC controller factory lambda
	/// (compiled at startup) never tries to inject it from the DI container.
	/// </summary>
	private IJwtService? JwtService => HttpContext.RequestServices.GetService<IJwtService>();

	/// <summary>
	/// Name of the httpOnly cookie that carries the refresh token. The cookie is scoped to
	/// /api/auth so it is only sent on auth endpoints (refresh), never on regular API calls.
	/// </summary>
	public const string RefreshCookieName = "sharpmush_refresh";

	/// <summary>
	/// Stores the refresh token in an httpOnly cookie so the WASM client never has to hold
	/// it in JavaScript-accessible memory (per the architectural decision: JWT in WASM
	/// memory only, refresh via httpOnly cookie). The token is also returned in the JSON
	/// body for non-browser clients.
	/// </summary>
	private void SetRefreshCookie(string refreshToken)
	{
		var lifetimeDays = HttpContext.RequestServices
			.GetService<Microsoft.Extensions.Options.IOptions<JwtOptions>>()?.Value.RefreshTokenLifetimeDays ?? 7;

		Response.Cookies.Append(RefreshCookieName, refreshToken, new CookieOptions
		{
			HttpOnly = true,
			Secure = true,
			SameSite = SameSiteMode.Strict,
			Path = "/api/auth",
			MaxAge = TimeSpan.FromDays(lifetimeDays),
		});
	}

	/// <summary>Removes the refresh cookie (logout / invalid refresh).</summary>
	private void ClearRefreshCookie() =>
		Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = "/api/auth" });

	/// <summary>Request body for OTT issuance via MUSH character credentials.</summary>
	public record MushTokenRequest(string? PlayerName, string? Password, string? AccountSessionToken, int? CharacterKey, long? CharacterCreationTime);

	/// <summary>Response body containing the one-time token.</summary>
	public record MushTokenResponse(string Token, int ExpiresIn);

	/// <summary>
	/// Validate MUSH credentials and issue a one-time login token.
	/// Accepts either:<br/>
	/// - Character credentials: <c>{ PlayerName, Password }</c><br/>
	/// - Account session: <c>{ AccountSessionToken, CharacterKey, CharacterCreationTime }</c>
	/// </summary>
	[HttpPost("mush-token")]
	[EnableRateLimiting("public-api")]
	// accountId is the account's internal GUID identifier, not a secret — it is derived from the
	// opaque session token for the purpose of fetching characters, not stored as cleartext sensitive data.
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "accountId is a non-secret GUID identifier derived from the session token for service lookups, not a password or key.")]
	public async Task<IActionResult> GetMushToken([FromBody] MushTokenRequest request)
	{
		// Path A: Account session token + character reference
		if (!string.IsNullOrWhiteSpace(request.AccountSessionToken) && request.CharacterKey.HasValue)
		{
			var accountId = await accountSessionStore.ValidateAsync(request.AccountSessionToken);
			if (accountId is null)
			{
				logger.LogInformation("OTT via account session: invalid or expired session token");
				return Unauthorized("Invalid or expired account session.");
			}

			// Verify that this character belongs to the account
			var characters = await accountService.GetCharactersAsync(accountId);
			var character = characters.FirstOrDefault(c => c.Object.Key == request.CharacterKey.Value);
			if (character is null)
			{
				logger.LogInformation("OTT via account session: character #{Key} not linked to the requesting account", request.CharacterKey.Value);
				return Unauthorized("Character is not linked to this account.");
			}

			var charRef = new DBRef(character.Object.Key, character.Object.CreationTime);
			const int sessionTtl = 60;
			var sessionToken = await ottStore.CreateTokenAsync(charRef, TimeSpan.FromSeconds(sessionTtl));

			logger.LogInformation("Issued OTT for character {Name} (#{Key}) via account session", character.Object.Name, character.Object.Key);
			return Ok(new MushTokenResponse(sessionToken, sessionTtl));
		}

		// Path B: Direct character credentials
		if (string.IsNullOrWhiteSpace(request.PlayerName))
			return BadRequest("PlayerName or AccountSessionToken is required.");

		var player = await mediator
			.CreateStream(new GetPlayerQuery(request.PlayerName))
			.FirstOrDefaultAsync();

		if (player is null)
		{
			logger.LogInformation("OTT request: player {Name} not found", Sanitize(request.PlayerName));
			return Unauthorized("Invalid credentials.");
		}

		var valid = passwordService.PasswordIsValid(
			$"#{player.Object.Key}:{player.Object.CreationTime}",
			request.Password ?? string.Empty,
			player.PasswordHash);

		if (!valid && !string.IsNullOrEmpty(player.PasswordHash))
		{
			logger.LogInformation("OTT request: invalid password for player {Name}", Sanitize(request.PlayerName));
			return Unauthorized("Invalid credentials.");
		}

		// Rehash legacy PennMUSH passwords on successful login
		if (valid && passwordService.NeedsRehash(player.PasswordHash))
		{
			await passwordService.RehashPasswordAsync(player, request.Password ?? string.Empty);
			logger.LogInformation("Rehashed legacy password for player #{Key} via OTT login", player.Object.Key);
		}

		const int ttlSeconds = 60;
		var playerRef = new DBRef(player.Object.Key, player.Object.CreationTime);
		var token = await ottStore.CreateTokenAsync(playerRef, TimeSpan.FromSeconds(ttlSeconds));

		logger.LogInformation("Issued OTT for player {Name} (#{Key})", player.Object.Name, player.Object.Key);
		return Ok(new MushTokenResponse(token, ttlSeconds));
	}

	/// <summary>Request body for account login.</summary>
	public record AccountLoginRequest(string UsernameOrEmail, string Password);

	/// <summary>Character summary included in account login/register responses.</summary>
	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);

	/// <summary>Response body for account login and registration.</summary>
	public record AccountLoginResponse(string AccountId, string Username, IReadOnlyList<CharacterSummary> Characters, string AccountSessionToken, bool MustChangePassword);

	/// <summary>
	/// Authenticate to an account (by username or email) and get the character list.
	/// Returns an account session token valid for 15 minutes (sliding window).
	/// </summary>
	[HttpPost("account-login")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> AccountLogin([FromBody] AccountLoginRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("UsernameOrEmail and Password are required.");

		var account = await accountService.AuthenticateAsync(request.UsernameOrEmail, request.Password);
		if (account is null)
		{
			logger.LogInformation("Account login failed for {Identifier}", Sanitize(request.UsernameOrEmail));
			return Unauthorized("Invalid account credentials.");
		}

		var characters = await accountService.GetCharactersAsync(account.Id!);
		var charSummaries = await BuildCharacterSummariesAsync(characters);

		var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15));
		logger.LogInformation("Account login success for {Username} ({Id})", Sanitize(account.Username), Sanitize(account.Id));
		return Ok(new AccountLoginResponse(account.Id!, account.Username, charSummaries, sessionToken, account.MustChangePassword));
	}

	/// <summary>Request body for account registration.</summary>
	public record AccountRegisterRequest(string Username, string? Email, string Password);

	/// <summary>
	/// Create a new account and return an account session. Email is optional.
	/// </summary>
	[HttpPost("account-register")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> AccountRegister([FromBody] AccountRegisterRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Username and Password are required.");

		var result = await accountService.CreateAccountAsync(request.Username, request.Email, request.Password);
		if (result.IsT1)
			return Conflict(result.AsT1.Value);

		var account = result.AsT0;
		var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15));

		logger.LogInformation("Account registered: {Username} ({Id})", Sanitize(account.Username), Sanitize(account.Id));
		return Ok(new AccountLoginResponse(account.Id!, account.Username, [], sessionToken, account.MustChangePassword));
	}

	/// <summary>Response body for the debug OTT endpoint.</summary>
	public record DebugOttResponse(string Token, int ExpiresIn, string PlayerName,
		string? AccountId, string? AccountUsername, string? AccountSessionToken, bool AccountMustChangePassword);

	/// <summary>
	/// Development-only endpoint: issue a one-time token for player #1 without credentials.
	/// Also returns the account session for the account linked to #1 (if one exists).
	/// Requires DebugAuth (automatically active in development mode).
	/// </summary>
	[HttpGet("debug-ott")]
	[Authorize]
	public async Task<IActionResult> GetDebugOtt()
	{
		var obj = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
		if (!obj.IsT0)
		{
			logger.LogWarning("Debug OTT: #1 is not a player or does not exist");
			return NotFound("Player #1 not found.");
		}

		var player = obj.AsPlayer;
		var playerRef = new DBRef(player.Object.Key, player.Object.CreationTime);
		const int ttl = 60;
		var token = await ottStore.CreateTokenAsync(playerRef, TimeSpan.FromSeconds(ttl));

		// Look up the account linked to #1 (created by BootstrapService)
		var account = await accountService.GetAccountForCharacterAsync(playerRef);
		string? accountSessionToken = null;
		if (account is not null)
			accountSessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15));

		logger.LogInformation("Debug OTT issued for {Name} (#{Key}), account: {AccountId}",
			Sanitize(player.Object.Name), player.Object.Key, Sanitize(account?.Id ?? "none"));

		return Ok(new DebugOttResponse(token, ttl, player.Object.Name,
			account?.Id, account?.Username, accountSessionToken, account?.MustChangePassword ?? false));
	}

	// -----------------------------------------------------------------------
	// JWT endpoints
	// -----------------------------------------------------------------------

	/// <summary>Request body for JWT login.</summary>
	public record JwtLoginRequest(string UsernameOrEmail, string Password, int CharacterKey, long CharacterCreationTime);

	/// <summary>Response body for JWT login / switch / refresh.</summary>
	public record JwtTokenResponse(string AccessToken, string RefreshToken, int ExpiresIn, string Role);

	/// <summary>
	/// Authenticate with account credentials and a specific character, returning a JWT token pair.
	/// Requires JWT auth to be configured (Jwt:SigningKey in appsettings).
	/// </summary>
	[HttpPost("jwt-login")]
	[AllowAnonymous]
	[EnableRateLimiting("public-api")]
	// account.Id is a non-secret GUID identifier used for service lookups, not a password or secret value.
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "account.Id is a non-secret GUID identifier used for service lookups, not a password or secret value.")]
	public async Task<IActionResult> JwtLogin([FromBody] JwtLoginRequest request)
	{
		if (JwtService is null)
			return StatusCode(501, "JWT authentication is not configured on this server.");

		var account = await accountService.AuthenticateAsync(request.UsernameOrEmail, request.Password);
		if (account is null)
		{
			logger.LogInformation("JWT login failed for {Identifier}", Sanitize(request.UsernameOrEmail));
			return Unauthorized("Invalid account credentials.");
		}

		var characters = await accountService.GetCharactersAsync(account.Id!);
		var character = characters.FirstOrDefault(c =>
			c.Object.Key == request.CharacterKey
			&& c.Object.CreationTime == request.CharacterCreationTime);

		if (character is null)
		{
			logger.LogInformation("JWT login: character #{Key} not linked to account {AccountId}",
				request.CharacterKey, Sanitize(account.Id));
			return Unauthorized("Character is not linked to this account.");
		}

		var flags = await character.Object.Flags.Value.ToListAsync();
		var role = roleDerivation.DeriveRole(character.Object.Key, flags);
		var result = await JwtService.IssueTokensAsync(account, character, role);

		logger.LogInformation("JWT issued for {Username} ({AccountId}), character #{Key}, role {Role}",
			Sanitize(account.Username), Sanitize(account.Id), character.Object.Key, role);
		SetRefreshCookie(result.RefreshToken);
		return Ok(new JwtTokenResponse(result.AccessToken, result.RefreshToken, result.ExpiresIn, result.Role.ToString()));
	}

	/// <summary>Request body for switching the active character (JWT).</summary>
	public record JwtSwitchCharacterRequest(string AccountSessionToken, int CharacterKey, long CharacterCreationTime);

	/// <summary>
	/// Switch to a different character under the same account and return a new JWT pair.
	/// Accepts the account-session token issued by <see cref="AccountLogin"/> (not a JWT).
	/// </summary>
	[HttpPost("jwt-switch-character")]
	[AllowAnonymous]
	[EnableRateLimiting("public-api")]
	// accountId is a non-secret GUID identifier derived from the session token for service lookups, not a password or secret value.
	[SuppressMessage("Security", "cs/cleartext-storage-of-sensitive-information",
		Justification = "accountId is a non-secret GUID identifier derived from the session token for service lookups, not a password or secret value.")]
	public async Task<IActionResult> JwtSwitchCharacter([FromBody] JwtSwitchCharacterRequest request)
	{
		if (JwtService is null)
			return StatusCode(501, "JWT authentication is not configured on this server.");

		var accountId = await accountSessionStore.ValidateAsync(request.AccountSessionToken);
		if (accountId is null)
		{
			logger.LogInformation("JWT switch-character: invalid or expired session token");
			return Unauthorized("Invalid or expired account session.");
		}

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || account.IsDisabled)
			return Unauthorized("Account not found or disabled.");

		var characters = await accountService.GetCharactersAsync(accountId);
		var character = characters.FirstOrDefault(c =>
			c.Object.Key == request.CharacterKey
			&& c.Object.CreationTime == request.CharacterCreationTime);

		if (character is null)
		{
			logger.LogInformation("JWT switch-character: character #{Key} not linked to account {AccountId}",
				request.CharacterKey, Sanitize(account.Id));
			return Unauthorized("Character is not linked to this account.");
		}

		var flags = await character.Object.Flags.Value.ToListAsync();
		var role = roleDerivation.DeriveRole(character.Object.Key, flags);
		var result = await JwtService.IssueTokensAsync(account, character, role);

		logger.LogInformation("JWT switch-character: issued for {AccountId}, character #{Key}, role {Role}",
			Sanitize(account.Id), character.Object.Key, role);
		SetRefreshCookie(result.RefreshToken);
		return Ok(new JwtTokenResponse(result.AccessToken, result.RefreshToken, result.ExpiresIn, result.Role.ToString()));
	}

	/// <summary>Request body for JWT refresh. RefreshToken may be omitted when the
	/// httpOnly refresh cookie set at login is present.</summary>
	public record JwtRefreshRequest(string? RefreshToken);

	/// <summary>
	/// Exchange a refresh token for a new JWT access/refresh token pair (single-use).
	/// The token is taken from the request body when supplied, otherwise from the
	/// httpOnly refresh cookie set by the login/switch endpoints — browser clients
	/// can refresh silently without ever holding the refresh token in script.
	/// </summary>
	[HttpPost("jwt-refresh")]
	[AllowAnonymous]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> JwtRefresh([FromBody] JwtRefreshRequest? request = null)
	{
		if (JwtService is null)
			return StatusCode(501, "JWT authentication is not configured on this server.");

		var refreshToken = !string.IsNullOrWhiteSpace(request?.RefreshToken)
			? request.RefreshToken
			: Request.Cookies[RefreshCookieName];

		if (string.IsNullOrWhiteSpace(refreshToken))
			return BadRequest("RefreshToken is required (body or refresh cookie).");

		var result = await JwtService.RefreshAsync(refreshToken);
		if (result is null)
		{
			logger.LogInformation("JWT refresh rejected: invalid or expired refresh token");
			ClearRefreshCookie();
			return Unauthorized("Invalid or expired refresh token.");
		}

		SetRefreshCookie(result.RefreshToken);
		return Ok(new JwtTokenResponse(result.AccessToken, result.RefreshToken, result.ExpiresIn, result.Role.ToString()));
	}

	private static async Task<IReadOnlyList<CharacterSummary>> BuildCharacterSummariesAsync(IReadOnlyList<SharpPlayer> characters)
	{
		var summaries = new List<CharacterSummary>();
		foreach (var c in characters)
		{
			var flagString = string.Join(" ", await c.Object.Flags.Value
				.Select(f => f.Name)
				.ToListAsync());
			summaries.Add(new CharacterSummary(c.Object.Key, c.Object.CreationTime, c.Object.Name, flagString));
		}
		return summaries;
	}

	/// <summary>
	/// Strip newlines and control characters from user-supplied strings before logging
	/// to prevent log injection (CodeQL cs/log-injection).
	/// </summary>
	private static string Sanitize(string? value) =>
		string.IsNullOrEmpty(value)
			? "(empty)"
			: new string(value.Where(c => !char.IsControl(c)).ToArray());
}

