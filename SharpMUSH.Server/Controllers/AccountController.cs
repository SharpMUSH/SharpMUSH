using System.Text.Json;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Authentication.Passkeys;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Account management endpoints. All routes require a valid <c>AccountSessionToken</c>
/// supplied as <c>Authorization: Bearer &lt;token&gt;</c>.
/// </summary>
[ApiController]
[Route("api/account")]
public class AccountController(
	IMediator mediator,
	IAccountService accountService,
	IAccountSessionStore accountSessionStore,
	IOptionsWrapper<SharpMUSHOptions> options,
	IValidateService validateService,
	PasskeyService passkeys,
	IPortalThemeService themes,
	BearerAccountResolver bearer,
	ILogger<AccountController> logger) : ControllerBase
{
	/// <summary>
	/// The session behind this request, or null when there is no usable bearer. Used to mark which
	/// roster entry the caller is acting as — the session token is opaque to the client, so the roster
	/// response is where a reloaded tab finds out.
	/// </summary>
	private async Task<IAccountSessionStore.SessionIdentity?> SessionAsync()
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return null;

		return await accountSessionStore.ValidateAsync(header["Bearer ".Length..].Trim());
	}

	/// <summary>Runs <paramref name="action"/> as the request's signed-in account, or answers why it is refused.</summary>
	private async Task<IActionResult> AsAccountAsync(Func<string, Task<IActionResult>> action, bool allowMustChangePassword = false)
		=> await bearer.ResolveAsync(this, allowMustChangePassword) switch
		{
			SignedInAccount account => await action(account.Id),
			ActionResult refused => refused
		};

	/// <summary>List all characters linked to the authenticated account.</summary>
	[HttpGet("characters")]
	public Task<IActionResult> GetCharacters()
		=> AsAccountAsync(async accountId =>
		{
			var characters = await accountService.GetCharactersAsync(accountId);
			// Resolved through the same rule the authentication handler applies, so the roster's
			// isActing flag can never disagree with the identity a write actually runs as.
			var acting = AccountSessionAuthenticationHandler.TryGetAccount(User, out _, out _)
				? AccountSessionAuthenticationHandler.ActingCharacter(User) is { } claimed
					? characters.FirstOrDefault(c => c.Object.DBRef == claimed)
					: null
				: ActingCharacterResolver.Resolve(await SessionAsync(), characters);
			var summaries = await CharacterSummaryMapper.BuildSummariesAsync(characters,
				actingKey: acting?.Object.Key, actingCreationTime: acting?.Object.CreationTime, themes: themes);
			return Ok(summaries);
		});

	public record CreateCharacterRequest(string Name, string Password);

	/// <summary>Create a new character and link it to the authenticated account.</summary>
	[HttpPost("characters")]
	public Task<IActionResult> CreateCharacter([FromBody] CreateCharacterRequest request)
		=> AsAccountAsync(async accountId =>
		{
			if (!options.CurrentValue.Net.PlayerCreation)
				return StatusCode(StatusCodes.Status403Forbidden, "Player creation is disabled on this server.");

			if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Password))
				return BadRequest("Name and Password are required.");

			// The connect screen's rule: ok_player_name(name, NOTHING, NOTHING), with bsd.c's refusals.
			if (await mediator.CreateStream(new GetPlayerQuery(request.Name))
					.AnyAsync(x => x.Object.Name.Equals(request.Name, StringComparison.InvariantCultureIgnoreCase)))
				return Conflict("There is already a player with that name.");

			if (!await validateService.ValidPlayerName(MarkupText.Plain(request.Name), new None(), new None()))
				return BadRequest("That name is not allowed.");

			try
			{
				var defaultHome = options.CurrentValue.Database.DefaultHome;
				var startingQuota = (int)options.CurrentValue.Limit.StartingQuota;
				var playerRef = await mediator.Send(new CreatePlayerCommand(
					request.Name, request.Password,
					new DBRef((int)defaultHome), new DBRef((int)defaultHome),
					startingQuota));

				await accountService.LinkCharacterAsync(accountId, playerRef);

				logger.LogInformation("Account {AccountId}: created character {Name} (#{Key}) via API", LogSanitizer.Sanitize(accountId), LogSanitizer.Sanitize(request.Name), playerRef.Number);
				return Ok(new { DbrefNumber = playerRef.Number, CreationTime = playerRef.CreationMilliseconds, Flags = await CreatedFlagsAsync(playerRef) });
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Character creation failed for account {AccountId}", LogSanitizer.Sanitize(accountId));
				return BadRequest(ex.Message);
			}
		});

	/// <summary>
	/// A new character's flags, as the roster carries them: the portal adds the row to the account's list itself,
	/// and without them it showed the character flagless until the list was refreshed. The character already
	/// exists and is linked, so failing to read them answers with none rather than failing the creation.
	/// </summary>
	private async Task<string> CreatedFlagsAsync(DBRef playerRef)
	{
		try
		{
			return await mediator.Send(new GetObjectNodeQuery(playerRef)) is AnySharpObject and SharpPlayer player
				? (await CharacterSummaryMapper.BuildSummariesAsync([player]))[0].Flags
				: string.Empty;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Created character #{Key}, but could not read its flags", playerRef.Number);
			return string.Empty;
		}
	}

	public record LinkCharacterRequest(string CharacterName, string CharacterPassword);

	/// <summary>
	/// Claims an EXISTING character for the authenticated account: one made with <c>create</c>, <c>@pcreate</c>
	/// or a database import, proven by the character's own password. Counterpart to
	/// <see cref="CreateCharacter"/>, which creates a brand-new character. A character with no password
	/// cannot be claimed; staff link those.
	/// </summary>
	[HttpPost("link-character")]
	public Task<IActionResult> LinkCharacter([FromBody] LinkCharacterRequest request)
		=> AsAccountAsync(async accountId =>
		{
			if (string.IsNullOrWhiteSpace(request.CharacterName))
				return BadRequest("CharacterName is required.");

			return await accountService.ClaimCharacterAsync(accountId, request.CharacterName.Trim(), request.CharacterPassword ?? string.Empty) switch
			{
				SharpPlayer player => await ClaimedAsync(player),
				LinkedElsewhere => Conflict("Character is already linked to another account."),
				Library.DiscriminatedUnions.NotFound => ClaimRefused(),
			};
		});

	private async Task<IActionResult> ClaimedAsync(SharpPlayer player)
	{
		logger.LogInformation("Linked existing character #{Key} to the requesting account", player.Object.Key);
		return Ok(new
		{
			DbrefNumber = player.Object.Key,
			player.Object.CreationTime,
			player.Object.Name,
			Flags = await CreatedFlagsAsync(player.Object.DBRef)
		});
	}

	private UnauthorizedObjectResult ClaimRefused()
	{
		logger.LogInformation("link-character refused: no character with that name and password");
		return Unauthorized("Invalid character credentials.");
	}

	/// <summary>Unlink a character from the authenticated account.</summary>
	[HttpDelete("characters/{dbrefNumber:int}")]
	public Task<IActionResult> UnlinkCharacter(int dbrefNumber)
		=> AsAccountAsync(async accountId =>
		{
			if (await accountService.UnlinkCharacterAsync(accountId, new DBRef(dbrefNumber)) is Error<string> refused)
				return Conflict(refused.Value);
			logger.LogInformation("Account {AccountId}: unlinked character #{Key}", LogSanitizer.Sanitize(accountId), dbrefNumber);
			return NoContent();
		});

	/// <summary>
	/// Sets one of the account's characters' portal theme and accent. Any of them, not only the acting one: the
	/// Theme settings page lists them all so a player can tell their tabs apart before opening them.
	/// </summary>
	[HttpPut("characters/{dbrefNumber:int}/appearance")]
	public Task<IActionResult> SetAppearance(int dbrefNumber, [FromBody] CharacterAppearance appearance)
		=> AsAccountAsync(async accountId =>
		{
			var characters = await accountService.GetCharactersAsync(accountId);
			if (characters.FirstOrDefault(c => c.Object.Key == dbrefNumber) is not { } character)
				return NotFound();

			return await themes.SetAppearanceAsync(character.Object, appearance) switch
			{
				CharacterAppearance stored => Ok(stored),
				Error<string> error => BadRequest(error.Value),
			};
		});

	public record ChangePasswordRequest(string OldPassword, string NewPassword);

	/// <summary>Change the account password.</summary>
	[HttpPut("password")]
	public Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
		=> AsAccountAsync(async accountId =>
		{
			var result = await accountService.ChangePasswordAsync(accountId, request.OldPassword, request.NewPassword);
			return result switch
			{
				Success => NoContent(),
				Error<string> err => Unauthorized(err.Value)
			};
		}, allowMustChangePassword: true);

	public record ChangeEmailRequest(string? NewEmail, string CurrentPassword);

	/// <summary>Add, change, or remove the account email. Send <c>null</c> for <c>NewEmail</c> to clear.</summary>
	[HttpPut("email")]
	public Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
		=> AsAccountAsync(async accountId =>
		{
			var result = await accountService.ChangeEmailAsync(accountId, request.NewEmail, request.CurrentPassword);
			return result switch
			{
				Success => NoContent(),
				Error<string> err when err.Value.Contains("already registered", StringComparison.OrdinalIgnoreCase)
					=> Conflict(err.Value),
				Error<string> err => Unauthorized(err.Value)
			};
		});

	public record ChangeUsernameRequest(string NewUsername);

	/// <summary>Change the account username.</summary>
	[HttpPut("username")]
	public Task<IActionResult> ChangeUsername([FromBody] ChangeUsernameRequest request)
		=> AsAccountAsync(async accountId =>
		{
			var result = await accountService.ChangeUsernameAsync(accountId, request.NewUsername);
			return result switch
			{
				Success => NoContent(),
				Error<string> err => Conflict(err.Value)
			};
		});

	/// <summary>A passkey as the account's owner sees it. <paramref name="Id"/> is its credential id, base64url.</summary>
	public record PasskeySummary(string Id, string Name, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool IsSynced);

	private static PasskeySummary Summarize(AccountPasskey passkey)
		=> new(PasskeyService.IdOf(passkey), passkey.Name, passkey.CreatedAt, passkey.LastUsedAt, passkey.IsBackedUp);

	/// <summary>The account's passkeys, oldest first.</summary>
	[HttpGet("passkeys")]
	public Task<IActionResult> GetPasskeys()
		=> AsAccountAsync(async accountId =>
		{
			var held = await passkeys.ListAsync(accountId);
			return Ok(held.Select(Summarize).ToList());
		});

	public record PasskeyOptionsRequest(string CurrentPassword);

	/// <summary>
	/// Starts adding a passkey: the options the browser hands to <c>navigator.credentials.create</c>. Asks
	/// for the password the holder signs in with, so a session alone cannot plant a passkey of its own.
	/// </summary>
	[HttpPost("passkeys/options")]
	[EnableRateLimiting("public-api")]
	public Task<IActionResult> PasskeyOptions([FromBody] PasskeyOptionsRequest request)
		=> AsAccountAsync(async accountId =>
		{
			var account = await accountService.GetByIdAsync(accountId);
			if (account is null) return Unauthorized("Account not found or not active.");

			if (await accountService.AuthenticateAsync(account.Username, request.CurrentPassword ?? string.Empty) is not SharpAccount confirmed
				|| confirmed.Id != account.Id)
				return Unauthorized("Current password is incorrect.");

			return await passkeys.BeginRegistrationAsync(account, Request, HttpContext.RequestAborted) switch
			{
				PasskeyService.Challenge challenge => Ok(challenge),
				Error<string> error => BadRequest(error.Value),
			};
		});

	public record AddPasskeyRequest(string? CeremonyId, string? Name, JsonElement Credential);

	/// <summary>Finishes adding a passkey with the browser's new credential.</summary>
	[HttpPost("passkeys")]
	public Task<IActionResult> AddPasskey([FromBody] AddPasskeyRequest request)
		=> AsAccountAsync(async accountId =>
		{
			return await passkeys.CompleteRegistrationAsync(accountId, request.CeremonyId, request.Name, request.Credential,
					HttpContext.RequestAborted) switch
			{
				AccountPasskey passkey => Ok(Summarize(passkey)),
				Error<string> error => BadRequest(error.Value),
			};
		});

	public record RenamePasskeyRequest(string Name);

	/// <summary>Renames one of the account's passkeys.</summary>
	[HttpPut("passkeys/{id}")]
	public Task<IActionResult> RenamePasskey(string id, [FromBody] RenamePasskeyRequest request)
		=> AsAccountAsync(async accountId =>
		{
			var name = request.Name?.Trim();
			if (string.IsNullOrEmpty(name))
				return BadRequest("A passkey needs a name.");
			if (name.Length > PasskeyService.MaxNameLength)
				return BadRequest($"A passkey's name can be at most {PasskeyService.MaxNameLength} characters.");

			return PasskeyService.CredentialIdOf(id) is { } credentialId
				&& await passkeys.RenameAsync(accountId, credentialId, name)
					? NoContent()
					: NotFound("No such passkey on this account.");
		});

	/// <summary>Removes one of the account's passkeys. It can no longer sign in.</summary>
	[HttpDelete("passkeys/{id}")]
	public Task<IActionResult> RemovePasskey(string id)
		=> AsAccountAsync(async accountId =>
		{
			if (PasskeyService.CredentialIdOf(id) is not { } credentialId
				|| !await passkeys.RemoveAsync(accountId, credentialId))
				return NotFound("No such passkey on this account.");

			return NoContent();
		});

	/// <summary>
	/// Invalidate the current account session token (logout), and the browser's remembered login so a
	/// tab opened later does not sign straight back in.
	/// </summary>
	[HttpPost("logout")]
	public async Task<IActionResult> Logout()
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			var token = header["Bearer ".Length..].Trim();
			await accountSessionStore.RevokeAsync(token);
		}
		if (RememberedLoginCookie.Read(Request) is { } remembered)
			await accountSessionStore.RevokeAsync(remembered);
		RememberedLoginCookie.Delete(Response);
		return NoContent();
	}
}
