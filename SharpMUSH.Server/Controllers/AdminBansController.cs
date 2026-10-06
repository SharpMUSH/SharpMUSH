using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// Account bans, behind <c>players.moderate</c>. A ban disables the account and keeps the reason, the
/// staff member and an optional expiry; <see cref="Services.BanExpiryService"/> lifts it when it runs out.
/// Host bans stay in the sitelock (<c>config.admin</c>); the listing carries its rules read-only.
///
/// Routes:
///   GET    api/admin/bans          — every ban, and the sitelock's host rules
///   POST   api/admin/bans          — ban an account
///   DELETE api/admin/bans/{key}    — lift an account's ban
/// </summary>
[ApiController]
[Route("api/admin/bans")]
[Authorize(Policy = PortalPermission.PlayersModerate)]
public class AdminBansController(
	IAccountService accounts,
	IPermissionService permissions,
	IVisibleWorldProjection projection,
	IOptionsWrapper<SharpMUSHOptions> options,
	IAuditLog audit,
	TimeProvider time) : ControllerBase
{
	public AdminBansController(
		IAccountService accounts,
		IPermissionService permissions,
		IVisibleWorldProjection projection,
		IOptionsWrapper<SharpMUSHOptions> options,
		IAuditLog audit)
		: this(accounts, permissions, projection, options, audit, TimeProvider.System)
	{
	}

	/// <summary>The longest reason kept.</summary>
	public const int MaxReasonLength = 500;

	private static string FullId(string key) => $"node_accounts/{key}";
	private static string KeyOf(string accountId) => accountId.Split('/')[^1];

	[HttpGet]
	public async Task<IActionResult> List(CancellationToken ct)
	{
		var rows = new List<AdminBanRow>();
		foreach (var ban in await accounts.GetBansAsync(ct))
		{
			if (await accounts.GetByIdAsync(ban.AccountId, ct) is not { } account) continue;
			var bannedBy = ban.BannedBy is { } by ? (await accounts.GetByIdAsync(by, ct))?.Username : null;
			var characters = await accounts.GetCharactersAsync(ban.AccountId, ct);
			rows.Add(new AdminBanRow(KeyOf(ban.AccountId), account.Username,
				characters.Select(c => c.Object.Name).Order(StringComparer.OrdinalIgnoreCase).ToList(),
				ban.Reason, bannedBy, ban.At, ban.ExpiresAt));
		}

		var hosts = options.CurrentValue.SitelockRules.Rules
			.Select(rule => new AdminHostRuleRow(rule.Key, rule.Value))
			.OrderBy(rule => rule.Pattern, StringComparer.OrdinalIgnoreCase)
			.ToList();
		return Ok(new AdminBansResponse(rows.OrderByDescending(row => row.At).ToList(), hosts));
	}

	[HttpPost]
	public async Task<IActionResult> Ban([FromBody] AdminBanRequest request, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(request.Reason))
			return BadRequest(new ApiErrorDto("Give a reason for the ban."));
		if (request.Reason.Length > MaxReasonLength)
			return BadRequest(new ApiErrorDto($"Keep the reason to {MaxReasonLength} characters."));
		var now = time.GetUtcNow();
		if (request.ExpiresAt is { } expires && expires <= now)
			return BadRequest(new ApiErrorDto("The ban's end has to be in the future."));

		if (User.FindFirstValue(ClaimTypes.NameIdentifier) is not { Length: > 0 } actorAccount)
			return Unauthorized();
		if (await User.ResolveExecutorAsync(projection, ct) is not { } executor)
			return Conflict(new ApiErrorDto("Choose a character to act as before banning anyone."));
		if (await accounts.GetByIdAsync(FullId(request.AccountKey), ct) is not { } account)
			return NotFound(new ApiErrorDto($"No account with key '{request.AccountKey}'."));
		if (account.Id == actorAccount)
			return Conflict(new ApiErrorDto("You cannot ban your own account."));

		// A ban reaches every character on the account, so the staff member has to control each one:
		// a royal cannot ban a wizard's account, nor anyone God's.
		foreach (var character in await accounts.GetCharactersAsync(account.Id!, ct))
		{
			if (!await permissions.Controls(executor, character))
				return StatusCode(StatusCodes.Status403Forbidden,
					new ApiErrorDto($"You do not control {character.Object.Name}, who is on that account."));
		}

		var reason = request.Reason.Trim();
		var result = await accounts.BanAsync(new AccountBan(account.Id!, reason, actorAccount, now, request.ExpiresAt), ct);
		if (result is Error<string> error)
			return Conflict(new ApiErrorDto(error.Value));

		await audit.RecordPortalAsync(User, AuditActions.BanAdd, AuditTargets.Of(account),
			AuditLog.WithReason(request.ExpiresAt is { } until ? $"until {until.UtcDateTime:yyyy-MM-dd HH:mm} UTC" : "no expiry",
				reason), ct);
		return NoContent();
	}

	[HttpDelete("{key}")]
	public async Task<IActionResult> Lift(string key, CancellationToken ct)
	{
		if (await accounts.GetByIdAsync(FullId(key), ct) is not { } account)
			return NotFound(new ApiErrorDto($"No account with key '{key}'."));
		if (await accounts.LiftBanAsync(account.Id!, ct) is Library.DiscriminatedUnions.NotFound)
			return NotFound(new ApiErrorDto($"{account.Username} is not banned."));

		await audit.RecordPortalAsync(User, AuditActions.BanLift, AuditTargets.Of(account), ct: ct);
		return NoContent();
	}
}
