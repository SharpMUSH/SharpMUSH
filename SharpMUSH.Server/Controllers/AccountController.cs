using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Server.Helpers;

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
	IPasswordService passwordService,
	IOptionsWrapper<SharpMUSHOptions> options,
	ILogger<AccountController> logger) : ControllerBase
{
	/// <summary>
	/// Resolves the account session bearer. Unless <paramref name="allowMustChangePassword"/>,
	/// accounts flagged MustChangePassword are rejected with 403 — the flag is enforced
	/// server-side, not advisory: a flagged session may only change its password or log out.
	/// </summary>
	private async Task<(string? AccountId, IActionResult? Failure)> GetAccountIdFromBearerAsync(bool allowMustChangePassword = false)
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return (null, Unauthorized("Invalid or expired account session."));

		var token = header["Bearer ".Length..].Trim();
		var accountId = await accountSessionStore.ValidateAsync(token);
		if (accountId is null)
			return (null, Unauthorized("Invalid or expired account session."));

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || account.IsDisabled)
			return (null, Unauthorized("Account not found or disabled."));

		if (!allowMustChangePassword && account.MustChangePassword)
			return (null, StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action."));

		return (accountId, null);
	}

	/// <summary>List all characters linked to the authenticated account.</summary>
	[HttpGet("characters")]
	public async Task<IActionResult> GetCharacters()
	{
		var (accountId, failure) = await GetAccountIdFromBearerAsync();
		if (failure is not null) return failure;

		var characters = await accountService.GetCharactersAsync(accountId!);
		var summaries = await CharacterSummaryMapper.BuildSummariesAsync(characters);
		return Ok(summaries);
	}

	public record CreateCharacterRequest(string Name, string Password);

	/// <summary>Create a new character and link it to the authenticated account.</summary>
	[HttpPost("characters")]
	public async Task<IActionResult> CreateCharacter([FromBody] CreateCharacterRequest request)
	{
		var (accountId, failure) = await GetAccountIdFromBearerAsync();
		if (failure is not null) return failure;

		if (!options.CurrentValue.Net.PlayerCreation)
			return StatusCode(StatusCodes.Status403Forbidden, "Player creation is disabled on this server.");

		if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Password))
			return BadRequest("Name and Password are required.");

		try
		{
			var defaultHome = options.CurrentValue.Database.DefaultHome;
			var startingQuota = (int)options.CurrentValue.Limit.StartingQuota;
			var playerRef = await mediator.Send(new CreatePlayerCommand(
				request.Name, request.Password,
				new DBRef((int)defaultHome), new DBRef((int)defaultHome),
				startingQuota));

			await accountService.LinkCharacterAsync(accountId!, playerRef);

			logger.LogInformation("Account {AccountId}: created character {Name} (#{Key}) via API", LogSanitizer.Sanitize(accountId), LogSanitizer.Sanitize(request.Name), playerRef.Number);
			return Ok(new { DbrefNumber = playerRef.Number, CreationTime = playerRef.CreationMilliseconds });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Character creation failed for account {AccountId}", LogSanitizer.Sanitize(accountId));
			return BadRequest(ex.Message);
		}
	}

	public record LinkCharacterRequest(string CharacterName, string CharacterPassword);

	/// <summary>
	/// Link an EXISTING character to the authenticated account by verifying the
	/// character's MUSH password. Counterpart to <see cref="CreateCharacter"/>,
	/// which creates a brand-new character.
	/// </summary>
	[HttpPost("link-character")]
	public async Task<IActionResult> LinkCharacter([FromBody] LinkCharacterRequest request)
	{
		var (accountId, failure) = await GetAccountIdFromBearerAsync();
		if (failure is not null) return failure;

		if (string.IsNullOrWhiteSpace(request.CharacterName))
			return BadRequest("CharacterName is required.");

		var player = await mediator
			.CreateStream(new GetPlayerQuery(request.CharacterName))
			.FirstOrDefaultAsync();

		if (player is null)
		{
			logger.LogInformation("Account {AccountId}: link-character failed — character not found", LogSanitizer.Sanitize(accountId));
			return Unauthorized("Invalid character credentials.");
		}

		var valid = passwordService.PasswordIsValid(
			$"#{player.Object.Key}:{player.Object.CreationTime}",
			request.CharacterPassword ?? string.Empty,
			player.PasswordHash);

		// Mirror the OTT login rule: a character with no stored password hash is
		// linkable without one; a wrong password against a real hash is rejected.
		if (!valid && !string.IsNullOrEmpty(player.PasswordHash))
		{
			logger.LogInformation("Account {AccountId}: link-character failed — bad password for #{Key}",
				LogSanitizer.Sanitize(accountId), player.Object.Key);
			return Unauthorized("Invalid character credentials.");
		}

		if (valid && passwordService.NeedsRehash(player.PasswordHash))
		{
			await passwordService.RehashPasswordAsync(player, request.CharacterPassword ?? string.Empty);
			logger.LogInformation("Rehashed legacy password for player #{Key} via link-character", player.Object.Key);
		}

		var charRef = new DBRef(player.Object.Key, player.Object.CreationTime);

		var existingOwner = await accountService.GetAccountForCharacterAsync(charRef);
		if (existingOwner is not null && existingOwner.Id != accountId)
			return Conflict("Character is already linked to another account.");

		await accountService.LinkCharacterAsync(accountId!, charRef);

		logger.LogInformation("Account {AccountId}: linked existing character #{Key}", LogSanitizer.Sanitize(accountId), player.Object.Key);
		return Ok(new { DbrefNumber = player.Object.Key, CreationTime = player.Object.CreationTime, player.Object.Name });
	}

	/// <summary>Unlink a character from the authenticated account.</summary>
	[HttpDelete("characters/{dbrefNumber:int}")]
	public async Task<IActionResult> UnlinkCharacter(int dbrefNumber)
	{
		var (accountId, failure) = await GetAccountIdFromBearerAsync();
		if (failure is not null) return failure;

		await accountService.UnlinkCharacterAsync(accountId!, new DBRef(dbrefNumber));
		logger.LogInformation("Account {AccountId}: unlinked character #{Key}", LogSanitizer.Sanitize(accountId), dbrefNumber);
		return NoContent();
	}

	public record ChangePasswordRequest(string OldPassword, string NewPassword);

	/// <summary>Change the account password.</summary>
	[HttpPut("password")]
	public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
	{
		var (accountId, failure) = await GetAccountIdFromBearerAsync(allowMustChangePassword: true);
		if (failure is not null) return failure;

		var result = await accountService.ChangePasswordAsync(accountId!, request.OldPassword, request.NewPassword);
		return result.Match<IActionResult>(
			_ => NoContent(),
			err => Unauthorized(err.Value));
	}

	public record ChangeEmailRequest(string? NewEmail, string CurrentPassword);

	/// <summary>Add, change, or remove the account email. Send <c>null</c> for <c>NewEmail</c> to clear.</summary>
	[HttpPut("email")]
	public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
	{
		var (accountId, failure) = await GetAccountIdFromBearerAsync();
		if (failure is not null) return failure;

		var result = await accountService.ChangeEmailAsync(accountId!, request.NewEmail, request.CurrentPassword);
		return result.Match<IActionResult>(
			_ => NoContent(),
			err => err.Value.Contains("already registered", StringComparison.OrdinalIgnoreCase)
				? Conflict(err.Value)
				: Unauthorized(err.Value));
	}

	public record ChangeUsernameRequest(string NewUsername);

	/// <summary>Change the account username.</summary>
	[HttpPut("username")]
	public async Task<IActionResult> ChangeUsername([FromBody] ChangeUsernameRequest request)
	{
		var (accountId, failure) = await GetAccountIdFromBearerAsync();
		if (failure is not null) return failure;

		var result = await accountService.ChangeUsernameAsync(accountId!, request.NewUsername);
		return result.Match<IActionResult>(
			_ => NoContent(),
			err => Conflict(err.Value));
	}

	/// <summary>Invalidate the current account session token (logout).</summary>
	[HttpPost("logout")]
	public async Task<IActionResult> Logout()
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			var token = header["Bearer ".Length..].Trim();
			await accountSessionStore.RevokeAsync(token);
		}
		return NoContent();
	}
}
