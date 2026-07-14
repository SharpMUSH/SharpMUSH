using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Helpers;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Admin-only account management. Authenticates with the account session bearer
/// (same scheme as AccountController) and requires the account's derived role to be
/// Wizard or God — enforced server-side per request, independent of the portal UI gate.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
public class AdminAccountsController(
	IAccountService accountService,
	IAccountSessionStore accountSessionStore,
	AccountClaimsService accountClaims,
	ILogger<AdminAccountsController> logger) : ControllerBase
{
	public record AdminCharacterSummary(int DbrefNumber, string Name);
	public record AdminAccountRow(string Id, string Username, string? Email, bool IsDisabled,
		bool MustChangePassword, IReadOnlyList<AdminCharacterSummary> Characters);
	public record ResetPasswordRequest(string NewPassword);

	private static string FullId(string key) => $"node_accounts/{key}";
	private static string KeyOf(SharpAccount account) => account.Id!.Split('/')[^1];

	private async Task<(string? AdminAccountId, IActionResult? Failure)> RequireWizardAsync()
	{
		var header = Request.Headers.Authorization.FirstOrDefault();
		if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
			return (null, Unauthorized("Invalid or expired account session."));

		var accountId = await accountSessionStore.ValidateAsync(header["Bearer ".Length..].Trim());
		if (accountId is null)
			return (null, Unauthorized("Invalid or expired account session."));

		var account = await accountService.GetByIdAsync(accountId);
		if (account is null || account.IsDisabled)
			return (null, Unauthorized("Account not found or disabled."));
		if (account.MustChangePassword)
			return (null, StatusCode(StatusCodes.Status403Forbidden, "Password change required before this action."));

		var role = await accountClaims.ComputeAccountRoleAsync(accountId);
		if (role < PortalRole.Wizard)
			return (null, StatusCode(StatusCodes.Status403Forbidden, "Wizard role required."));

		return (accountId, null);
	}

	[HttpGet]
	public async Task<IActionResult> List([FromQuery] string? search = null)
	{
		var (_, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;

		var accounts = await accountService.GetAllAccountsAsync();
		if (!string.IsNullOrWhiteSpace(search))
			accounts = accounts.Where(a =>
				a.Username.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| (a.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

		var rows = new List<AdminAccountRow>();
		foreach (var account in accounts)
		{
			var characters = await accountService.GetCharactersAsync(account.Id!);
			rows.Add(new AdminAccountRow(KeyOf(account), account.Username, account.Email,
				account.IsDisabled, account.MustChangePassword,
				characters.Select(c => new AdminCharacterSummary(c.Object.Key, c.Object.Name)).ToList()));
		}
		return Ok(rows);
	}

	[HttpPost("{key}/reset-password")]
	public async Task<IActionResult> ResetPassword(string key, [FromBody] ResetPasswordRequest request)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;
		if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
			return BadRequest("NewPassword must be at least 8 characters.");

		var result = await accountService.SetPasswordAsync(FullId(key), request.NewPassword, mustChangePassword: true);
		if (result.IsT1) return NotFound(result.AsT1.Value);
		await accountSessionStore.RevokeAllForAccountAsync(FullId(key));
		logger.LogInformation("Admin {AdminId} reset password for account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
		return NoContent();
	}

	[HttpPost("{key}/disable")]
	public async Task<IActionResult> Disable(string key)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;
		var result = await accountService.DisableAccountAsync(FullId(key));
		if (result.IsT1) return NotFound(result.AsT1.Value);
		logger.LogInformation("Admin {AdminId} disabled account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
		return NoContent();
	}

	[HttpPost("{key}/enable")]
	public async Task<IActionResult> Enable(string key)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;
		var result = await accountService.EnableAccountAsync(FullId(key));
		if (result.IsT1) return NotFound(result.AsT1.Value);
		logger.LogInformation("Admin {AdminId} enabled account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
		return NoContent();
	}

	[HttpDelete("{key}/characters/{dbrefNumber:int}")]
	public async Task<IActionResult> UnlinkCharacter(string key, int dbrefNumber)
	{
		var (adminId, failure) = await RequireWizardAsync();
		if (failure is not null) return failure;
		await accountService.UnlinkCharacterAsync(FullId(key), new DBRef(dbrefNumber));
		logger.LogInformation("Admin {AdminId} unlinked #{Dbref} from account {Key}", LogSanitizer.Sanitize(adminId), dbrefNumber, LogSanitizer.Sanitize(key));
		return NoContent();
	}
}
