using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

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
	ILogger<AuthController> logger) : ControllerBase
{
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
				logger.LogInformation("OTT via account session: character #{Key} not linked to account {AccountId}", request.CharacterKey.Value, accountId);
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
			logger.LogInformation("OTT request: player {Name} not found", request.PlayerName);
			return Unauthorized("Invalid credentials.");
		}

		var valid = passwordService.PasswordIsValid(
			$"#{player.Object.Key}:{player.Object.CreationTime}",
			request.Password ?? string.Empty,
			player.PasswordHash);

		if (!valid && !string.IsNullOrEmpty(player.PasswordHash))
		{
			logger.LogInformation("OTT request: invalid password for player {Name}", request.PlayerName);
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
	public record AccountLoginResponse(string AccountId, string Username, IReadOnlyList<CharacterSummary> Characters, string AccountSessionToken);

	/// <summary>
	/// Authenticate to an account (by username or email) and get the character list.
	/// Returns an account session token valid for 15 minutes (sliding window).
	/// </summary>
	[HttpPost("account-login")]
	public async Task<IActionResult> AccountLogin([FromBody] AccountLoginRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("UsernameOrEmail and Password are required.");

		var account = await accountService.AuthenticateAsync(request.UsernameOrEmail, request.Password);
		if (account is null)
		{
			logger.LogInformation("Account login failed for {Identifier}", request.UsernameOrEmail);
			return Unauthorized("Invalid account credentials.");
		}

		var characters = await accountService.GetCharactersAsync(account.Id!);
		var charSummaries = await BuildCharacterSummariesAsync(characters);

		var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15));
		logger.LogInformation("Account login success for {Username} ({Id})", account.Username, account.Id);
		return Ok(new AccountLoginResponse(account.Id!, account.Username, charSummaries, sessionToken));
	}

	/// <summary>Request body for account registration.</summary>
	public record AccountRegisterRequest(string Username, string? Email, string Password);

	/// <summary>
	/// Create a new account and return an account session. Email is optional.
	/// </summary>
	[HttpPost("account-register")]
	public async Task<IActionResult> AccountRegister([FromBody] AccountRegisterRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Username and Password are required.");

		var result = await accountService.CreateAccountAsync(request.Username, request.Email, request.Password);
		if (result.IsT1)
			return Conflict(result.AsT1.Value);

		var account = result.AsT0;
		var sessionToken = await accountSessionStore.CreateTokenAsync(account.Id!, TimeSpan.FromMinutes(15));

		logger.LogInformation("Account registered: {Username} ({Id})", account.Username, account.Id);
		return Ok(new AccountLoginResponse(account.Id!, account.Username, [], sessionToken));
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
}

