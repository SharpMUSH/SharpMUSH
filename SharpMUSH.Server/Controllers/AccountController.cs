using Microsoft.Extensions.DependencyInjection;
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
	ILogger<AccountController> logger,
	AccountSecurityService? security = null, AccountSecurityCeremonyStore? securityCeremonies = null) : ControllerBase
{
	private AccountSecurityService Security => security ?? HttpContext.RequestServices.GetRequiredService<AccountSecurityService>();
	private AccountSecurityCeremonyStore Ceremonies => securityCeremonies ?? HttpContext.RequestServices.GetRequiredService<AccountSecurityCeremonyStore>();
	private string RawSession => SharpMUSH.Server.Authentication.External.ExternalLoginJourney.Bearer(HttpContext) ?? "";

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

	public record CreateCharacterRequest(string Name, string? Password);

	/// <summary>Create a new character and link it to the authenticated account.</summary>
	[HttpPost("characters")]
	public Task<IActionResult> CreateCharacter([FromBody] CreateCharacterRequest request,
		[FromServices] SharpMUSH.Library.Services.AccountCredentialGate? credentialGate = null,
		[FromServices] IPasswordService? passwords = null, CancellationToken cancellationToken = default)
		=> (credentialGate ?? new SharpMUSH.Library.Services.AccountCredentialGate()).RunAsync(() => AsAccountAsync(async accountId =>
		{
			if (!options.CurrentValue.Net.PlayerCreation)
				return StatusCode(StatusCodes.Status403Forbidden, "Player creation is disabled on this server.");
			if (string.IsNullOrWhiteSpace(request.Name))
				return BadRequest("Enter a name for your character.");
			if (request.Password is not null && string.IsNullOrWhiteSpace(request.Password))
				return BadRequest("Enter a password for your character, or leave it out to sign in through your account.");
			if (request.Password is null && passwords is null)
				return StatusCode(StatusCodes.Status503ServiceUnavailable, "Character credential service is unavailable.");
			if (await mediator.CreateStream(new GetPlayerQuery(request.Name), cancellationToken)
					.AnyAsync(x => x.Object.Name.Equals(request.Name, StringComparison.InvariantCultureIgnoreCase), cancellationToken))
				return Conflict("There is already a player with that name.");
			if (!await validateService.ValidPlayerName(MarkupText.Plain(request.Name), new None(), new None()))
				return BadRequest("That name is not allowed.");

			DBRef? created = null;
			try
			{
				// Hash and discard an unreachable random credential before dispatch, so neither an empty hash
				// nor the random plaintext can escape through a queued command or diagnostics.
				var password = request.Password;
				string? salt = null;
				if (password is null && passwords is { } passwordService)
				{
					password = passwordService.HashPassword(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64)));
					if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Character password hashing failed.");
					salt = "";
				}
				var defaultHome = options.CurrentValue.Database.DefaultHome;
				var playerRef = await mediator.Send(new CreatePlayerCommand(request.Name, password ?? throw new InvalidOperationException("Character credential missing."),
					new DBRef((int)defaultHome), new DBRef((int)defaultHome), (int)options.CurrentValue.Limit.StartingQuota, Salt: salt), cancellationToken);
				created = playerRef;
				await accountService.LinkCharacterAsync(accountId, playerRef, cancellationToken);
				if (await accountService.GetAccountForCharacterAsync(playerRef, cancellationToken) is not { } holder || holder.Id != accountId)
					throw new InvalidOperationException("Character ownership attachment failed.");
				logger.LogInformation("Account {AccountId}: created character {Name} (#{Key}) via API", LogSanitizer.Sanitize(accountId), LogSanitizer.Sanitize(request.Name), playerRef.Number);
				return Ok(new { DbrefNumber = playerRef.Number, CreationTime = playerRef.CreationMilliseconds, Flags = await CreatedFlagsAsync(playerRef) });
			}
			catch (Exception error)
			{
				if (created is { } character)
				{
					var heldBy = await accountService.GetAccountForCharacterAsync(character, CancellationToken.None);
					if (heldBy is null || heldBy.Id == accountId)
						await mediator.Send(new DeleteObjectCommand(character), CancellationToken.None);
				}
				logger.LogWarning("Character creation failed for account {AccountId}: {ErrorType}", LogSanitizer.Sanitize(accountId), error.GetType().Name);
				return BadRequest("Character creation could not be completed.");
			}
		}), cancellationToken);

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

	public record ChangePasswordRequest(string OldPassword, string NewPassword, string? ProofGrant = null);

	/// <summary>Change the account password.</summary>
	[HttpPut("password")]
	public Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
		=> AsAccountAsync(async accountId =>
		{
			var result = await Security.PasswordAsync(HttpContext, accountId, request.NewPassword, request.ProofGrant, request.OldPassword, HttpContext.RequestAborted);
			return result switch { Success => NoContent(), Error<string> error => Unauthorized(error.Value) };
		}, allowMustChangePassword: true);

	public record ChangeEmailRequest(string? NewEmail, string CurrentPassword, string? ProofGrant = null);

	/// <summary>Add, change, or remove the account email. Send <c>null</c> for <c>NewEmail</c> to clear.</summary>
	[HttpPut("email")]
	public Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
		=> AsAccountAsync(async accountId =>
		{
			var result = await Security.EmailAsync(HttpContext, accountId, request.NewEmail, request.ProofGrant, request.CurrentPassword, HttpContext.RequestAborted);
			return result switch { Success => NoContent(), Error<string> error => Unauthorized(error.Value) };
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

	public record PasskeyOptionsRequest(string CurrentPassword, string? ProofGrant = null);

	/// <summary>
	/// Starts adding a passkey: the options the browser hands to <c>navigator.credentials.create</c>. Asks
	/// for the password the holder signs in with, so a session alone cannot plant a passkey of its own.
	/// </summary>
	[HttpPost("passkeys/options")]
	[EnableRateLimiting("public-api")]
	public Task<IActionResult> PasskeyOptions([FromBody] PasskeyOptionsRequest request)
		=> AsAccountAsync(async accountId =>
		{
			var result = await Security.WithProofAsync<PasskeyService.Challenge>(HttpContext, accountId, AccountSecurityOperation.EnrollPasskey,
				request.ProofGrant, request.CurrentPassword, async (account, authority) =>
				{
					var result = await passkeys.BeginRegistrationAsync(account, Request, HttpContext.RequestAborted);
					if (result is PasskeyService.Challenge challenge && !Ceremonies.Begin(challenge.CeremonyId, accountId, RawSession, account.CredentialEpoch, AccountSecurityOperation.EnrollPasskey, authority))
						return new Error<string>("Too many pending security requests.");
					return result;
				}, HttpContext.RequestAborted);
			return result switch { PasskeyService.Challenge challenge => Ok(challenge), Error<string> error => Unauthorized(error.Value) };
		});

	public record AddPasskeyRequest(string? CeremonyId, string? Name, JsonElement Credential);

	/// <summary>Finishes adding a passkey with the browser's new credential.</summary>
	[HttpPost("passkeys")]
	public Task<IActionResult> AddPasskey([FromBody] AddPasskeyRequest request)
		=> AsAccountAsync(async accountId => await HttpContext.RequestServices.GetRequiredService<SharpMUSH.Library.Services.AccountCredentialGate>().RunAsync<IActionResult>(async () =>
		{
			var binding = Ceremonies.Take(request.CeremonyId, accountId, RawSession);
			if (binding is null || binding.Authority is null || binding.Operation != AccountSecurityOperation.EnrollPasskey
								|| !await HttpContext.RequestServices.GetRequiredService<IAccountProofAuthorityValidator>().IsCurrentAsync(accountId, binding.Authority, HttpContext.RequestAborted)
				|| await accountSessionStore.InspectAsync(RawSession) is not IAccountSessionStore.SessionIdentity session || session.AccountId != accountId
				|| await accountService.GetByIdAsync(accountId) is not { IsActive: true, MustChangePassword: false } account || account.CredentialEpoch != binding.Epoch)
				return Unauthorized("The passkey enrollment expired. Start again.");
			return await passkeys.CompleteRegistrationAsync(accountId, request.CeremonyId, request.Name, request.Credential, HttpContext.RequestAborted) switch
			{ AccountPasskey passkey => await Registered(passkey), Error<string> error => BadRequest(error.Value) };
			Task<IActionResult> Registered(AccountPasskey passkey) => Task.FromResult<IActionResult>(Ok(Summarize(passkey)));
		}, HttpContext.RequestAborted));

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
	public Task<IActionResult> RemovePasskey(string id, [FromHeader(Name = "X-SharpMUSH-Proof")] string? proofGrant = null)
		=> AsAccountAsync(async accountId =>
		{
			if (PasskeyService.CredentialIdOf(id) is not { } credentialId) return NotFound();
			var result = await Security.WithProofAsync<Success>(HttpContext, accountId, AccountSecurityOperation.RemovePasskey, proofGrant, null, async (_, authority) =>
			{
				var result = await mediator.Send(new RemoveAccountPasskeyCommand(accountId, credentialId, await Security.EnabledAsync(HttpContext.RequestAborted)));
				if (result is Success) await Security.RevokeAndReplaceAsync(HttpContext, accountId, authority, HttpContext.RequestAborted);
				return result;
			}, HttpContext.RequestAborted);
			return result switch { Success => NoContent(), Error<string> error => Conflict(error.Value) };
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
