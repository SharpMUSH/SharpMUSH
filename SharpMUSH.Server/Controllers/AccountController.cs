using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

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
	ILogger<AccountController> logger) : ControllerBase
{
	private async Task<string?> GetAccountIdFromBearerAsync()
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return null;

		var token = header["Bearer ".Length..].Trim();
		return await accountSessionStore.ValidateAsync(token);
	}

	// ── Characters ─────────────────────────────────────────────────────────────

	public record CharacterSummary(int DbrefNumber, long CreationTime, string Name, string Flags);

	/// <summary>List all characters linked to the authenticated account.</summary>
	[HttpGet("characters")]
	public async Task<IActionResult> GetCharacters()
	{
		var accountId = await GetAccountIdFromBearerAsync();
		if (accountId is null) return Unauthorized("Invalid or expired account session.");

		var characters = await accountService.GetCharactersAsync(accountId);
		var summaries = await BuildSummariesAsync(characters);
		return Ok(summaries);
	}

	public record CreateCharacterRequest(string Name, string Password);

	/// <summary>Create a new character and link it to the authenticated account.</summary>
	[HttpPost("characters")]
	public async Task<IActionResult> CreateCharacter([FromBody] CreateCharacterRequest request)
	{
		var accountId = await GetAccountIdFromBearerAsync();
		if (accountId is null) return Unauthorized("Invalid or expired account session.");

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

			await accountService.LinkCharacterAsync(accountId, playerRef);

			logger.LogInformation("Account {AccountId}: created character {Name} (#{Key}) via API", accountId, request.Name, playerRef.Number);
			return Ok(new { DbrefNumber = playerRef.Number, CreationTime = playerRef.CreationMilliseconds });
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Character creation failed for account {AccountId}", accountId);
			return BadRequest(ex.Message);
		}
	}

	/// <summary>Unlink a character from the authenticated account.</summary>
	[HttpDelete("characters/{dbrefNumber:int}")]
	public async Task<IActionResult> UnlinkCharacter(int dbrefNumber)
	{
		var accountId = await GetAccountIdFromBearerAsync();
		if (accountId is null) return Unauthorized("Invalid or expired account session.");

		await accountService.UnlinkCharacterAsync(accountId, new DBRef(dbrefNumber));
		logger.LogInformation("Account {AccountId}: unlinked character #{Key}", accountId, dbrefNumber);
		return NoContent();
	}

	// ── Account Management ──────────────────────────────────────────────────────

	public record ChangePasswordRequest(string OldPassword, string NewPassword);

	/// <summary>Change the account password.</summary>
	[HttpPut("password")]
	public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
	{
		var accountId = await GetAccountIdFromBearerAsync();
		if (accountId is null) return Unauthorized("Invalid or expired account session.");

		var result = await accountService.ChangePasswordAsync(accountId, request.OldPassword, request.NewPassword);
		return result.Match<IActionResult>(
			_ => NoContent(),
			err => Unauthorized(err.Value));
	}

	public record ChangeEmailRequest(string? NewEmail, string CurrentPassword);

	/// <summary>Add, change, or remove the account email. Send <c>null</c> for <c>NewEmail</c> to clear.</summary>
	[HttpPut("email")]
	public async Task<IActionResult> ChangeEmail([FromBody] ChangeEmailRequest request)
	{
		var accountId = await GetAccountIdFromBearerAsync();
		if (accountId is null) return Unauthorized("Invalid or expired account session.");

		var result = await accountService.ChangeEmailAsync(accountId, request.NewEmail, request.CurrentPassword);
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
		var accountId = await GetAccountIdFromBearerAsync();
		if (accountId is null) return Unauthorized("Invalid or expired account session.");

		var result = await accountService.ChangeUsernameAsync(accountId, request.NewUsername);
		return result.Match<IActionResult>(
			_ => NoContent(),
			err => Conflict(err.Value));
	}

	// ── Logout ──────────────────────────────────────────────────────────────────

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

	private static async Task<IReadOnlyList<CharacterSummary>> BuildSummariesAsync(IReadOnlyList<SharpPlayer> characters)
	{
		var result = new List<CharacterSummary>();
		foreach (var c in characters)
		{
			var flags = string.Join(" ", await c.Object.Flags.Value.Select(f => f.Name).ToListAsync());
			result.Add(new CharacterSummary(c.Object.Key, c.Object.CreationTime, c.Object.Name, flags));
		}
		return result;
	}
}
