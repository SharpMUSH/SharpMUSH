using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Library.Logging;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Account management. Authenticates with the account session bearer (same scheme as
/// AccountController). Listing accounts and changing their status needs the Wizard role; resetting a
/// password and unlinking a character are moderation and need <c>players.moderate</c>. Both are enforced
/// server-side per request, independent of the portal UI gate, and every change is audited.
/// </summary>
[ApiController]
[Route("api/admin/accounts")]
[EnableRateLimiting("public-api")]
public class AdminAccountsController(
	IAccountService accountService,
	IAccountSessionStore accountSessionStore,
	AccountClaimsService accountClaims,
	IAdministrativeCapabilityService capabilities,
	IAuditLog audit,
	IMediator mediator,
	BearerAccountResolver bearer,
	ILogger<AdminAccountsController> logger) : ControllerBase
{
	public record AdminCharacterSummary(int DbrefNumber, string Name);
	/// <param name="IsReserved">
	/// True for a server-owned account whose status cannot be changed. Reported rather than derived
	/// client-side so the reservation rule lives only where it is enforced.
	/// </param>
	/// <param name="IsGodsAccount">
	/// True for the account holding God (#1), which may be restored but never disabled, closed or deleted.
	/// </param>
	public record AdminAccountRow(string Id, string Username, string? Email, string Status,
		bool MustChangePassword, bool IsReserved, bool IsGodsAccount, IReadOnlyList<AdminCharacterSummary> Characters);
	public record ResetPasswordRequest(string NewPassword);

	private static string FullId(string key) => $"node_accounts/{key}";

	private async Task<AuditTarget> AccountTargetAsync(string key)
		=> await accountService.GetByIdAsync(FullId(key)) is { } account
			? AuditTargets.Of(account)
			: AuditTargets.Of(AuditTargetKinds.Account, FullId(key));
	private static string KeyOf(SharpAccount account) => account.Id!.Split('/')[^1];

	/// <summary>Runs <paramref name="action"/> as the administering account when it holds the Wizard role, as the
	/// accounts page requires for the account list and status changes.</summary>
	private async Task<IActionResult> AsWizardAsync(Func<string, Task<IActionResult>> action)
		=> await bearer.ResolveAsync(this) switch
		{
			SignedInAccount admin when await IsWizardAsync(admin.Id) => await action(admin.Id),
			SignedInAccount => StatusCode(StatusCodes.Status403Forbidden, "Wizard role required."),
			ActionResult refused => refused
		};

	/// <summary>Runs <paramref name="action"/> as the administering account when it holds <c>players.moderate</c>,
	/// which password resets and unlinking a character need.</summary>
	private async Task<IActionResult> AsModeratorAsync(Func<string, Task<IActionResult>> action)
		=> await bearer.ResolveAsync(this) switch
		{
			SignedInAccount admin when await capabilities.AuthorizeAsync(new CapabilityActor(admin.Id), PortalPermission.PlayersModerate)
				=> await action(admin.Id),
			SignedInAccount => StatusCode(StatusCodes.Status403Forbidden, "players.moderate required."),
			ActionResult refused => refused
		};

	/// <summary>The AccountSession handler computed the role claim on this request; another scheme's is recomputed.</summary>
	private async Task<bool> IsWizardAsync(string accountId)
	{
		var role = AccountSessionAuthenticationHandler.TryGetAccount(User, out _, out _)
			&& Enum.TryParse<PortalRole>(User.FindFirstValue(ClaimTypes.Role), out var claimed)
				? claimed
				: await accountClaims.ComputeAccountRoleAsync(accountId);
		return role >= PortalRole.Wizard;
	}

	[HttpGet]
	public Task<IActionResult> List([FromQuery] string? search = null)
		=> AsWizardAsync(async adminId =>
		{
			var accounts = await accountService.GetAllAccountsAsync();
			if (!string.IsNullOrWhiteSpace(search))
				accounts = accounts.Where(a =>
					a.Username.Contains(search, StringComparison.OrdinalIgnoreCase)
					|| (a.Email?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

			var rows = await accounts.ToAsyncEnumerable()
				.Select(async (account, ct) => new AdminAccountRow(KeyOf(account), account.Username, account.Email,
					account.Status.ToString(), account.MustChangePassword, SystemAccount.IsReserved(account.Username),
					await accountService.IsGodsAccountAsync(account.Id!, ct),
					(await accountService.GetCharactersAsync(account.Id!, ct))
					.Select(c => new AdminCharacterSummary(c.Object.Key, c.Object.Name)).ToList()))
				.ToListAsync();
			return Ok(rows);
		});

	[HttpPost("{key}/reset-password")]
	public Task<IActionResult> ResetPassword(string key, [FromBody] ResetPasswordRequest request)
		=> AsModeratorAsync(async adminId =>
		{
			if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
				return BadRequest("NewPassword must be at least 8 characters.");

			var result = await accountService.SetPasswordAsync(FullId(key), request.NewPassword, mustChangePassword: true);
			if (result is Error<string> error) return NotFound(error.Value);
			await accountSessionStore.RevokeAllForAccountAsync(FullId(key));
			await audit.RecordPortalAsync(adminId, AuditActions.AccountPassword, await AccountTargetAsync(key));
			logger.LogInformation("Admin {AdminId} reset password for account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
			return NoContent();
		});

	[HttpPost("{key}/disable")]
	public Task<IActionResult> Disable(string key)
		=> AsWizardAsync(async adminId =>
		{
			if (await accountService.GetByIdAsync(FullId(key)) is null)
				return NotFound($"No account with key '{key}'.");
			var result = await accountService.DisableAccountAsync(FullId(key));
			if (result is Error<string> error) return Conflict(error.Value);
			await audit.RecordPortalAsync(adminId, AuditActions.AccountStatus, await AccountTargetAsync(key),
				nameof(AccountStatus.Disabled));
			logger.LogInformation("Admin {AdminId} disabled account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
			return NoContent();
		});

	[HttpPost("{key}/enable")]
	public Task<IActionResult> Enable(string key)
		=> AsWizardAsync(async adminId =>
		{
			var result = await accountService.EnableAccountAsync(FullId(key));
			if (result is Error<string> error) return NotFound(error.Value);
			await audit.RecordPortalAsync(adminId, AuditActions.AccountStatus, await AccountTargetAsync(key),
				nameof(AccountStatus.Active));
			logger.LogInformation("Admin {AdminId} enabled account {Key}", LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key));
			return NoContent();
		});

	public record SetStatusRequest(string Status);

	[HttpPost("{key}/status")]
	public Task<IActionResult> SetStatus(string key, [FromBody] SetStatusRequest request)
		=> AsWizardAsync(async adminId =>
		{
			if (!AccountStatusParser.TryParseName(request.Status, out var status))
				return BadRequest($"Unknown account status '{request.Status}'.");

			// Resolving the account here separates the two error causes: absent is 404, present but
			// refused (the reserved system account, or God's) is 409. Mapping both to NotFound claimed an account
			// does not exist when it does.
			var accountId = FullId(key);
			if (await accountService.GetByIdAsync(accountId) is null)
				return NotFound($"No account with key '{key}'.");

			var result = await accountService.SetAccountStatusAsync(accountId, status);
			if (result is Error<string> error) return Conflict(error.Value);
			await audit.RecordPortalAsync(adminId, AuditActions.AccountStatus, await AccountTargetAsync(key), status.ToString());

			logger.LogInformation("Admin {AdminId} set account {Key} status to {Status}",
				LogSanitizer.Sanitize(adminId), LogSanitizer.Sanitize(key), status);
			return NoContent();
		});

	[HttpDelete("{key}/characters/{dbrefNumber:int}")]
	public Task<IActionResult> UnlinkCharacter(string key, int dbrefNumber, [FromQuery] string? reason = null)
		=> AsModeratorAsync(async adminId =>
		{
			if (reason is { Length: > AdminBansController.MaxReasonLength })
				return BadRequest($"Keep the reason to {AdminBansController.MaxReasonLength} characters.");
			var character = await mediator.Send(new GetObjectNodeQuery(new DBRef(dbrefNumber)));
			if (await accountService.UnlinkCharacterAsync(FullId(key), new DBRef(dbrefNumber)) is Error<string> refused)
				return Conflict(refused.Value);
			await audit.RecordPortalAsync(adminId, AuditActions.CharacterUnlink,
				character is AnySharpObject unlinked
					? AuditTargets.Of(unlinked)
					: new AuditTarget(AuditTargetKinds.Character, $"#{dbrefNumber}", $"#{dbrefNumber}"),
				AuditLog.WithReason((await AccountTargetAsync(key)).Name, reason?.Trim()));
			logger.LogInformation("Admin {AdminId} unlinked #{Dbref} from account {Key}", LogSanitizer.Sanitize(adminId), dbrefNumber, LogSanitizer.Sanitize(key));
			return NoContent();
		});
}
