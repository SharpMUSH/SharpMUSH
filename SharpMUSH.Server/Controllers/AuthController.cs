using SharpMUSH.Library;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.Passkeys;
using SharpMUSH.Library.Logging;

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
	AccountClaimsService accountClaims,
	IOptionsWrapper<SharpMUSHOptions> options,
	IHostEnvironment environment,
	SitelockGuard sitelockGuard,
	PasskeyService passkeys,
	IPortalThemeService themes,
	ILogger<AuthController> logger) : ControllerBase
{
	/// <summary>The remote IP the current request originated from, for session origin tracking.</summary>
	private string ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

	/// <summary>The 403 body returned by every sitelock-blocked auth surface below.</summary>
	private const string SitelockedMessage = "Access from your location is restricted.";

	/// <summary>
	/// True if the requesting IP is sitelocked out of <paramref name="surfaceFlag"/> — callers return
	/// 403 (<see cref="SitelockedMessage"/>) when this is true. Web REST callers have no resolved
	/// client hostname (unlike telnet), so only the IP is matched — CIDR/bare-IP rules still apply;
	/// host-glob rules simply never match here.
	/// </summary>
	private bool IsSitelocked(string surfaceFlag) => sitelockGuard.IsBlocked(ClientIp(), host: "", surfaceFlag);

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
		if (IsSitelocked(SitelockGuard.Connect))
			return StatusCode(StatusCodes.Status403Forbidden, SitelockedMessage);

		if (!string.IsNullOrWhiteSpace(request.AccountSessionToken) && request.CharacterKey.HasValue)
		{
			var session = await accountSessionStore.ValidateAsync(request.AccountSessionToken);
			if (session is null)
			{
				logger.LogInformation("OTT via account session: invalid or expired session token");
				return Unauthorized("Invalid or expired account session.");
			}

			var accountId = session.Value.AccountId;

			var sessionAccount = await accountService.GetByIdAsync(accountId);
			if (sessionAccount is null || !sessionAccount.IsActive)
				return Unauthorized("Account not found or not active.");
			if (sessionAccount.MustChangePassword)
				return StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action.");

			var characters = await accountService.GetCharactersAsync(accountId);

			if (!options.CurrentValue.Net.Logins && !await AnyStaffCharacterAsync(characters))
				return StatusCode(StatusCodes.Status403Forbidden, "Logins are disabled.");

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

		if (string.IsNullOrWhiteSpace(request.PlayerName))
			return BadRequest("PlayerName or AccountSessionToken is required.");

		var player = await mediator
			.CreateStream(new GetPlayerQuery(request.PlayerName))
			.FirstOrDefaultAsync();

		if (player is null)
		{
			logger.LogInformation("OTT request: player {Name} not found", LogSanitizer.Sanitize(request.PlayerName));
			return Unauthorized("Invalid credentials.");
		}

		var valid = passwordService.PasswordIsValid(
			request.Password ?? string.Empty,
			player.PasswordHash);

		if (!valid && !string.IsNullOrEmpty(player.PasswordHash))
		{
			logger.LogInformation("OTT request: invalid password for player {Name}", LogSanitizer.Sanitize(request.PlayerName));
			return Unauthorized("Invalid credentials.");
		}

		if (valid && passwordService.NeedsRehash(player.PasswordHash))
		{
			await passwordService.RehashPasswordAsync(player, request.Password ?? string.Empty);
			logger.LogInformation("Rehashed legacy password for player #{Key} via OTT login", player.Object.Key);
		}

		if (!options.CurrentValue.Net.Logins)
		{
			if (!await new AnySharpObject(player).IsWizard())
				return StatusCode(StatusCodes.Status403Forbidden, "Logins are disabled.");
		}

		const int ttlSeconds = 60;
		var playerRef = new DBRef(player.Object.Key, player.Object.CreationTime);
		var token = await ottStore.CreateTokenAsync(playerRef, TimeSpan.FromSeconds(ttlSeconds));

		logger.LogInformation("Issued OTT for player {Name} (#{Key})", player.Object.Name, player.Object.Key);
		return Ok(new MushTokenResponse(token, ttlSeconds));
	}

	/// <summary>Request body for switching the active character via an authenticated account session.</summary>
	public record SwitchCharacterRequest(int CharacterKey, long CharacterCreationTime);

	/// <summary>
	/// Response body for a character switch: an OTT for the terminal, plus the session token the
	/// caller must adopt — that token, not anything the client sends per request, is what makes the
	/// server treat later calls as this character.
	/// </summary>
	public record SwitchCharacterResponse(string Ott, int ExpiresIn, string AccountSessionToken);

	/// <summary>
	/// Switch to a different character under the same account. Mints a NEW session token bound to the
	/// target character and returns it alongside an OTT for the terminal; the caller replaces its own
	/// stored token with it.
	/// </summary>
	/// <remarks>
	/// The previous token is deliberately NOT revoked — it is left to lapse on its own sliding TTL.
	/// A tab opened via <c>window.open</c> inherits a COPY of its opener's sessionStorage, so the new
	/// tab consuming an <c>?as=</c> hint calls this endpoint while its opener is still using the token
	/// it inherited from. Revoking here would log the opener out.
	/// </remarks>
	[HttpPost("switch-character")]
	[Authorize(AuthenticationSchemes = AccountSessionAuthenticationHandler.SchemeName)]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> SwitchCharacter([FromBody] SwitchCharacterRequest request)
	{
		if (IsSitelocked(SitelockGuard.Connect))
			return StatusCode(StatusCodes.Status403Forbidden, SitelockedMessage);

		var accountId = User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (accountId is null) return Unauthorized("Invalid or expired account session.");

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || !account.IsActive)
			return Unauthorized("Account not found or not active.");
		if (account.MustChangePassword)
			return StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action.");

		var characters = await accountService.GetCharactersAsync(accountId);

		if (!options.CurrentValue.Net.Logins && !await AnyStaffCharacterAsync(characters))
			return StatusCode(StatusCodes.Status403Forbidden, "Logins are disabled.");

		var character = characters.FirstOrDefault(c =>
			c.Object.Key == request.CharacterKey && c.Object.CreationTime == request.CharacterCreationTime);
		if (character is null)
			return Unauthorized("Character is not linked to this account.");

		const int ttl = 60;
		var ott = await ottStore.CreateTokenAsync(new DBRef(character.Object.Key, character.Object.CreationTime), TimeSpan.FromSeconds(ttl));
		var sessionToken = await accountSessionStore.CreateTokenAsync(accountId, TimeSpan.FromMinutes(15), ClientIp(),
			character.Object.Key, character.Object.CreationTime);

		logger.LogInformation("Session rebound to character {Name} (#{Key})", character.Object.Name, character.Object.Key);
		return Ok(new SwitchCharacterResponse(ott, ttl, sessionToken));
	}

	/// <summary>Request body for account login.</summary>
	/// <param name="RememberMe">Also keep the account signed in on this browser, in a
	/// <see cref="RememberedLoginCookie"/>, for tabs opened later.</param>
	public record AccountLoginRequest(string UsernameOrEmail, string Password, bool RememberMe = false);

	/// <summary>Response body for account login and registration.</summary>
	public record AccountLoginResponse(string AccountId, string Username,
		IReadOnlyList<CharacterSummaryMapper.CharacterSummary> Characters,
		string AccountSessionToken, bool MustChangePassword, string Role, IReadOnlyList<string> Permissions);

	/// <summary>
	/// Authenticate to an account (by username or email) and get the character list.
	/// Returns an account session token valid for 15 minutes (sliding window).
	/// </summary>
	[HttpPost("account-login")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> AccountLogin([FromBody] AccountLoginRequest request)
	{
		if (IsSitelocked(SitelockGuard.Connect))
			return StatusCode(StatusCodes.Status403Forbidden, SitelockedMessage);

		if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("UsernameOrEmail and Password are required.");

		switch (await accountService.AuthenticateAsync(request.UsernameOrEmail, request.Password))
		{
			case SharpAccount account:
				return await AccountLoggedInAsync(account, request.RememberMe);
			case AccountUnavailable unavailable:
				logger.LogInformation("Account login refused for {Identifier}: account is {Status}",
					LogSanitizer.Sanitize(request.UsernameOrEmail), unavailable.Account.Status);
				return StatusCode(StatusCodes.Status403Forbidden, unavailable.Message);
			default: // NotFound
				logger.LogInformation("Account login failed for {Identifier}", LogSanitizer.Sanitize(request.UsernameOrEmail));
				return Unauthorized("Invalid account credentials.");
		}
	}

	/// <summary>
	/// Starts a passkey sign-in: the options the browser hands to <c>navigator.credentials.get</c>, and
	/// the ceremony id <see cref="PasskeyLogin"/> is answered with.
	/// </summary>
	[HttpPost("passkey-login/options")]
	[EnableRateLimiting("public-api")]
	public IActionResult PasskeyLoginOptions()
	{
		if (IsSitelocked(SitelockGuard.Connect))
			return StatusCode(StatusCodes.Status403Forbidden, SitelockedMessage);

		return passkeys.BeginSignIn(Request) switch
		{
			PasskeyService.Challenge challenge => Ok(challenge),
			Error<string> error => BadRequest(error.Value),
		};
	}

	/// <summary>Request body for a passkey sign-in: the ceremony id and the browser's credential, as its JSON.</summary>
	/// <param name="RememberMe">As on <see cref="AccountLoginRequest"/>.</param>
	public record PasskeyLoginRequest(string? CeremonyId, JsonElement Credential, bool RememberMe = false);

	/// <summary>
	/// Signs in with a passkey, answering as <see cref="AccountLogin"/> does. The passkey names the account.
	/// </summary>
	[HttpPost("passkey-login")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> PasskeyLogin([FromBody] PasskeyLoginRequest request)
	{
		if (IsSitelocked(SitelockGuard.Connect))
			return StatusCode(StatusCodes.Status403Forbidden, SitelockedMessage);

		return await passkeys.CompleteSignInAsync(request.CeremonyId, request.Credential, HttpContext.RequestAborted) switch
		{
			SharpAccount { Status: AccountStatus.Deleted } => Unauthorized("This passkey is not registered here. It may have been removed from the account."),
			SharpAccount { IsActive: false } account => await PasskeyRefusedAsync(account),
			SharpAccount account => await AccountLoggedInAsync(account, request.RememberMe),
			Error<string> error => Unauthorized(error.Value),
		};
	}

	private async Task<IActionResult> PasskeyRefusedAsync(SharpAccount account)
	{
		var unavailable = await accountService.UnavailableAsync(account);
		logger.LogInformation("Passkey login refused for {Id}: account is {Status}", LogSanitizer.Sanitize(account.Id), account.Status);
		return StatusCode(StatusCodes.Status403Forbidden, unavailable.Message);
	}

	/// <param name="remember">Also start a remembered login for this browser.</param>
	/// <param name="preferredKey">The character to bind the session to, when the account still owns it;
	/// otherwise the primary character.</param>
	private async Task<IActionResult> AccountLoggedInAsync(SharpAccount account, bool remember,
		int? preferredKey = null, long? preferredCreationTime = null)
	{
		var characters = await accountService.GetCharactersAsync(account.Id!);

		if (!options.CurrentValue.Net.Logins && !await AnyStaffCharacterAsync(characters))
			return StatusCode(StatusCodes.Status403Forbidden, "Logins are disabled.");

		if (remember)
			await RememberAsync(account.Id!);

		var role = await accountClaims.ComputeAccountRoleAsync(account.Id!);
		var permissions = await accountClaims.ComputeGrantedScopesAsync(account.Id!);

		// Bind the session to the primary character up front, so there is never a "has characters but
		// the token names none" state for a request handler to paper over. Switching mints a new token.
		// The pick goes through ActingCharacterResolver so that this binding and the implicit
		// resolution a characterless session falls back to can never name different characters.
		var primary = characters.FirstOrDefault(c =>
				c.Object.Key == preferredKey && c.Object.CreationTime == preferredCreationTime)
			?? ActingCharacterResolver.Primary(characters);

		// The roster carries the binding too — the token is opaque to the client, so this response is
		// where the tab learns who it starts as.
		var charSummaries = await CharacterSummaryMapper.BuildSummariesAsync(characters,
			actingKey: primary?.Object.Key, actingCreationTime: primary?.Object.CreationTime, themes: themes);
		var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15), ClientIp(),
			primary?.Object.Key, primary?.Object.CreationTime);
		logger.LogInformation("Account login success for {Username} ({Id})", LogSanitizer.Sanitize(account.Username), LogSanitizer.Sanitize(account.Id));
		return Ok(new AccountLoginResponse(account.Id!, account.Username, charSummaries, sessionToken,
			account.MustChangePassword, role.ToString(), permissions.ToList()));
	}

	/// <summary>Request body for account registration.</summary>
	/// <param name="RememberMe">As on <see cref="AccountLoginRequest"/>.</param>
	public record AccountRegisterRequest(string Username, string? Email, string Password, bool RememberMe = false);

	/// <summary>
	/// Create a new account and return an account session. Email is optional.
	/// </summary>
	[HttpPost("account-register")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> AccountRegister([FromBody] AccountRegisterRequest request)
	{
		if (IsSitelocked(SitelockGuard.Create))
			return StatusCode(StatusCodes.Status403Forbidden, SitelockedMessage);

		if (!options.CurrentValue.Net.PlayerCreation)
			return StatusCode(StatusCodes.Status403Forbidden, "Player creation is disabled on this server.");

		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Username and Password are required.");

		return await accountService.CreateAccountAsync(request.Username, request.Email, request.Password) switch
		{
			SharpAccount account => await RegisteredAsync(account, request.RememberMe),
			Error<string> error => Conflict(error.Value),
		};
	}

	/// <summary>
	/// Mint the session a newly registered account signs in with.
	/// </summary>
	private async Task<IActionResult> RegisteredAsync(SharpAccount account, bool remember)
	{
		if (remember)
			await RememberAsync(account.Id!);

		var role = await accountClaims.ComputeAccountRoleAsync(account.Id!);
		var permissions = await accountClaims.ComputeGrantedScopesAsync(account.Id!);

		var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15), ClientIp());

		logger.LogInformation("Account registered: {Username} ({Id})", LogSanitizer.Sanitize(account.Username), LogSanitizer.Sanitize(account.Id));
		return Ok(new AccountLoginResponse(account.Id!, account.Username, [], sessionToken,
			account.MustChangePassword, role.ToString(), permissions.ToList()));
	}

	/// <summary>
	/// Starts a remembered login for <paramref name="accountId"/> and hands it to the browser. One per
	/// sign-in: a browser that signs in again gets a new one, and the one it replaced lapses on its own.
	/// </summary>
	private async Task RememberAsync(string accountId)
	{
		var token = await accountSessionStore.CreateRememberedLoginAsync(accountId, RememberedLoginCookie.Lifetime, ClientIp());
		RememberedLoginCookie.Write(Response, token);
	}

	/// <summary>Request body for <see cref="AccountResume"/>: the character the tab was playing, if any.</summary>
	public record AccountResumeRequest(int? CharacterKey, long? CharacterCreationTime);

	/// <summary>
	/// Signs a tab in from the browser's remembered login (<see cref="RememberedLoginCookie"/>): a tab
	/// opened after the last one closed, or one whose own session ran out while it sat idle. Answers like
	/// <see cref="AccountLogin"/>, with a new session bound to the requested character when the account
	/// still owns it, and renews the remembered login for another <see cref="RememberedLoginCookie.Lifetime"/>.
	/// </summary>
	[HttpPost("account-resume")]
	[EnableRateLimiting("public-api")]
	public async Task<IActionResult> AccountResume([FromBody] AccountResumeRequest? request)
	{
		if (IsSitelocked(SitelockGuard.Connect))
			return StatusCode(StatusCodes.Status403Forbidden, SitelockedMessage);

		if (RememberedLoginCookie.Read(Request) is not { } token)
			return Unauthorized("This browser has no remembered login.");

		if (await accountSessionStore.RedeemRememberedLoginAsync(token) is not { } accountId)
		{
			RememberedLoginCookie.Delete(Response);
			return Unauthorized("The remembered login has expired or was signed out.");
		}

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || !account.IsActive)
		{
			await accountSessionStore.RevokeAsync(token);
			RememberedLoginCookie.Delete(Response);
			return Unauthorized("Account not found or not active.");
		}

		RememberedLoginCookie.Write(Response, token);
		logger.LogInformation("Remembered login resumed for {Username} ({Id})",
			LogSanitizer.Sanitize(account.Username), LogSanitizer.Sanitize(account.Id));
		return await AccountLoggedInAsync(account, remember: false, request?.CharacterKey, request?.CharacterCreationTime);
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
		// Development-only. In production this endpoint must not exist even for
		// authenticated users — a valid player session must never mint a God OTT.
		if (!environment.IsDevelopment())
			return NotFound();

		if (await mediator.Send(new GetObjectNodeQuery(new DBRef(1))) is not (AnySharpObject and SharpPlayer player))
		{
			logger.LogWarning("Debug OTT: #1 is not a player or does not exist");
			return NotFound("Player #1 not found.");
		}

		var playerRef = new DBRef(player.Object.Key, player.Object.CreationTime);
		const int ttl = 60;
		var token = await ottStore.CreateTokenAsync(playerRef, TimeSpan.FromSeconds(ttl));

		// Look up the account linked to #1 (created by BootstrapService)
		var account = await accountService.GetAccountForCharacterAsync(playerRef);
		string? accountSessionToken = null;
		if (account is not null)
			accountSessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15), ClientIp(),
				player.Object.Key, player.Object.CreationTime);

		logger.LogInformation("Debug OTT issued for {Name} (#{Key}), account: {AccountId}",
			LogSanitizer.Sanitize(player.Object.Name), player.Object.Key, LogSanitizer.Sanitize(account?.Id ?? "none"));

		return Ok(new DebugOttResponse(token, ttl, player.Object.Name,
			account?.Id, account?.Username, accountSessionToken, account?.MustChangePassword ?? false));
	}

	/// <summary>
	/// PennMUSH semantics: an account qualifies for login while <c>Net.Logins</c> is disabled
	/// if ANY linked character is staff (character #1, or a wizard).
	/// </summary>
	private async Task<bool> AnyStaffCharacterAsync(IReadOnlyList<SharpPlayer> characters) =>
		await characters.ToAsyncEnumerable().AnyAsync(async (character, ct) =>
			await new AnySharpObject(character).IsWizard(ct));
}

