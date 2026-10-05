using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Records every role change <see cref="RoleManagementService"/> makes in the audit log. Every change,
/// from the portal and from the game, already goes through that service, so recording here covers both
/// once; a refused change is not recorded.
/// </summary>
public sealed class AuditingRoleManagementService(
	RoleManagementService inner,
	IAuditLog audit,
	IAccountService accounts) : IRoleManagementService
{
	public async Task<RoleOutcome<SharpRole>> SaveRoleAsync(RoleActor actor, RoleDraft draft, CancellationToken ct = default)
	{
		var outcome = await inner.SaveRoleAsync(actor, draft, ct);
		if (outcome is SharpRole role) await RecordAsync(actor, AuditActions.RoleSave, RoleTarget(role), Describe(draft), ct);
		return outcome;
	}

	public async Task<RoleOutcome<SharpRole>> EditRoleAsync(RoleActor actor, string slug, Func<SharpRole, RoleDraft> change,
		CancellationToken ct = default)
	{
		RoleDraft? applied = null;
		var outcome = await inner.EditRoleAsync(actor, slug, role => applied = change(role), ct);
		if (outcome is SharpRole saved)
			await RecordAsync(actor, AuditActions.RoleSave, RoleTarget(saved), applied is null ? null : Describe(applied), ct);
		return outcome;
	}

	public async Task<RoleOutcome<Success>> DeleteRoleAsync(RoleActor actor, string slug, CancellationToken ct = default)
	{
		var outcome = await inner.DeleteRoleAsync(actor, slug, ct);
		if (outcome is Success) await RecordAsync(actor, AuditActions.RoleDelete, AuditTargets.Of(AuditTargetKinds.Role, slug), null, ct);
		return outcome;
	}

	public async Task<RoleOutcome<Success>> AssignAsync(RoleActor actor, string accountId, string slug, CancellationToken ct = default)
	{
		var outcome = await inner.AssignAsync(actor, accountId, slug, ct);
		if (outcome is Success) await RecordAsync(actor, AuditActions.RoleAssign, await AccountTargetAsync(accountId, ct), slug, ct);
		return outcome;
	}

	public async Task<RoleOutcome<Success>> UnassignAsync(RoleActor actor, string accountId, string slug, CancellationToken ct = default)
	{
		var outcome = await inner.UnassignAsync(actor, accountId, slug, ct);
		if (outcome is Success) await RecordAsync(actor, AuditActions.RoleUnassign, await AccountTargetAsync(accountId, ct), slug, ct);
		return outcome;
	}

	public async Task<RoleOutcome<Success>> SetOverridesAsync(RoleActor actor, string accountId, IReadOnlyCollection<string> scopes,
		PermissionState state, CancellationToken ct = default)
	{
		var outcome = await inner.SetOverridesAsync(actor, accountId, scopes, state, ct);
		if (outcome is Success)
			await RecordAsync(actor, AuditActions.RoleOverride, await AccountTargetAsync(accountId, ct), Describe(scopes, state), ct);
		return outcome;
	}

	public async Task<RoleOutcome<Success>> AssignToObjectAsync(RoleActor actor, AnySharpObject target, string slug,
		CancellationToken ct = default)
	{
		var outcome = await inner.AssignToObjectAsync(actor, target, slug, ct);
		if (outcome is Success) await RecordAsync(actor, AuditActions.RoleAssign, AuditTargets.Of(target), slug, ct);
		return outcome;
	}

	public async Task<RoleOutcome<Success>> UnassignFromObjectAsync(RoleActor actor, AnySharpObject target, string slug,
		CancellationToken ct = default)
	{
		var outcome = await inner.UnassignFromObjectAsync(actor, target, slug, ct);
		if (outcome is Success) await RecordAsync(actor, AuditActions.RoleUnassign, AuditTargets.Of(target), slug, ct);
		return outcome;
	}

	public async Task<RoleOutcome<Success>> SetObjectOverridesAsync(RoleActor actor, AnySharpObject target,
		IReadOnlyCollection<string> scopes, PermissionState state, CancellationToken ct = default)
	{
		var outcome = await inner.SetObjectOverridesAsync(actor, target, scopes, state, ct);
		if (outcome is Success) await RecordAsync(actor, AuditActions.RoleOverride, AuditTargets.Of(target), Describe(scopes, state), ct);
		return outcome;
	}

	private ValueTask RecordAsync(RoleActor actor, string action, AuditTarget target, string? details, CancellationToken ct)
		=> actor switch
		{
			AnySharpObject executor => audit.RecordAsync(executor, action, target, details, ct),
			CapabilityActor account => audit.RecordPortalAsync(account.AccountId, action, target, details, ct),
		};

	private async ValueTask<AuditTarget> AccountTargetAsync(string accountId, CancellationToken ct)
		=> await accounts.GetByIdAsync(accountId, ct) is { } account
			? AuditTargets.Of(account)
			: AuditTargets.Of(AuditTargetKinds.Account, accountId);

	private static AuditTarget RoleTarget(SharpRole role) => new(AuditTargetKinds.Role, role.Slug, role.Name);

	private static string Describe(RoleDraft draft)
		=> $"priority {draft.Priority}; "
			+ string.Join(", ", draft.Permissions.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));

	private static string Describe(IReadOnlyCollection<string> scopes, PermissionState state)
		=> $"{state}: {string.Join(", ", scopes)}";
}
