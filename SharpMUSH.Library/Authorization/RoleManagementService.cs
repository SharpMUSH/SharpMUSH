using System.Text.RegularExpressions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Authorization;

/// <summary>Why a role change was refused, so each surface can answer in its own terms.</summary>
public enum RoleRefusalKind
{
	/// <summary>The request itself is malformed or asks for something no one may do.</summary>
	Invalid,

	/// <summary>The role or account does not exist.</summary>
	NotFound,

	/// <summary>The actor lacks the permission or the rank for it.</summary>
	Forbidden
}

/// <summary>A refused role change and the sentence explaining it.</summary>
public readonly record struct RoleRefusal(RoleRefusalKind Kind, string Message);

/// <summary>A role change's result, or why it was refused.</summary>
public union RoleOutcome<T>(T, RoleRefusal);

/// <summary>The editable fields of a role, as a create or an edit proposes them.</summary>
public sealed record RoleDraft(
	string Slug,
	string Name,
	string? Color,
	int Priority,
	IReadOnlyDictionary<string, PermissionState> Permissions);

/// <summary>
/// Every change to roles, assignments and per-account overrides, from the portal and from the game,
/// goes through here so both apply the same rules (<see cref="RoleHierarchy"/>):
/// <list type="bullet">
/// <item>Every change needs <see cref="PortalPermission.RolesAdmin"/>.</item>
/// <item>A role can be created, edited, deleted, assigned or removed only when it sits below the
/// actor's highest role, and only an account whose highest role is below the actor's can have its
/// roles or overrides changed.</item>
/// <item>A role or override can allow only scopes the actor holds.</item>
/// <item>Nobody but the owner changes their own roles or overrides.</item>
/// <item>System roles keep their slug and priority, cannot be deleted and are never assigned by hand.</item>
/// </list>
/// The owner (the account linked to player #1) is exempt from the hierarchy rules.
/// </summary>
public interface IRoleManagementService
{
	/// <summary>Creates a role, or replaces an existing role's editable fields.</summary>
	Task<RoleOutcome<SharpRole>> SaveRoleAsync(CapabilityActor actor, RoleDraft draft, CancellationToken ct = default);

	/// <summary>Edits an existing role: <paramref name="change"/> turns the stored role into a draft.</summary>
	Task<RoleOutcome<SharpRole>> EditRoleAsync(CapabilityActor actor, string slug, Func<SharpRole, RoleDraft> change, CancellationToken ct = default);

	/// <summary>Deletes a role and every assignment of it.</summary>
	Task<RoleOutcome<Success>> DeleteRoleAsync(CapabilityActor actor, string slug, CancellationToken ct = default);

	/// <summary>Gives an account a role.</summary>
	Task<RoleOutcome<Success>> AssignAsync(CapabilityActor actor, string accountId, string slug, CancellationToken ct = default);

	/// <summary>Takes a role away from an account.</summary>
	Task<RoleOutcome<Success>> UnassignAsync(CapabilityActor actor, string accountId, string slug, CancellationToken ct = default);

	/// <summary>Sets a per-account override; <see cref="PermissionState.Inherit"/> clears it.</summary>
	Task<RoleOutcome<Success>> SetOverrideAsync(CapabilityActor actor, string accountId, string scope, PermissionState state, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed partial class RoleManagementService(
	IRoleRegistryService registry,
	IAccountService accounts,
	IAdministrativeCapabilityService capabilities,
	IPermissionResolver resolver,
	IAccountClaimsInvalidator invalidator) : IRoleManagementService
{
	/// <summary>Validation and the write it guards run one at a time, so two changes cannot both pass a stale check.</summary>
	private readonly SemaphoreSlim _gate = new(1, 1);

	[GeneratedRegex("^[a-z0-9_-]{1,32}$")]
	private static partial Regex SlugPattern();

	[GeneratedRegex("^#[0-9a-fA-F]{6}$")]
	private static partial Regex ColorPattern();

	public Task<RoleOutcome<SharpRole>> SaveRoleAsync(CapabilityActor actor, RoleDraft draft, CancellationToken ct = default)
		=> Gated(async () => await SaveCoreAsync(actor, draft, ct), ct);

	public Task<RoleOutcome<SharpRole>> EditRoleAsync(CapabilityActor actor, string slug, Func<SharpRole, RoleDraft> change, CancellationToken ct = default)
		=> Gated(async () => await registry.GetRoleAsync(slug, ct) switch
		{
			SharpRole role => await SaveCoreAsync(actor, change(role) with { Slug = role.Slug }, ct),
			_ => Refuse<SharpRole>(RoleRefusalKind.NotFound, $"No role named '{slug}'.")
		}, ct);

	public Task<RoleOutcome<Success>> DeleteRoleAsync(CapabilityActor actor, string slug, CancellationToken ct = default)
		=> Gated(async () =>
		{
			if (await registry.GetRoleAsync(slug, ct) is not SharpRole role)
				return Refuse<Success>(RoleRefusalKind.NotFound, $"No role named '{slug}'.");
			if (role.IsSystem)
				return Refuse<Success>(RoleRefusalKind.Invalid, $"{role.Name} is a system role and cannot be deleted.");
			var me = await capabilities.GetContextAsync(actor, ct);
			if (Unauthorized(me) is { } refusal) return refusal;
			if (!RoleHierarchy.Outranks(me, role.Priority)) return NotBelow<Success>(role, me);

			var holders = await registry.GetAccountIdsForRoleAsync(role.Slug);
			foreach (var holder in holders)
				await registry.RemoveRoleFromAccountAsync(holder, role.Slug);
			await registry.RemoveRoleAsync(role.Slug);
			foreach (var holder in holders)
				await invalidator.InvalidateAsync(holder, ct);
			return new Success();
		}, ct);

	public Task<RoleOutcome<Success>> AssignAsync(CapabilityActor actor, string accountId, string slug, CancellationToken ct = default)
		=> ChangeAssignmentAsync(actor, accountId, slug, assign: true, ct);

	public Task<RoleOutcome<Success>> UnassignAsync(CapabilityActor actor, string accountId, string slug, CancellationToken ct = default)
		=> ChangeAssignmentAsync(actor, accountId, slug, assign: false, ct);

	public Task<RoleOutcome<Success>> SetOverrideAsync(CapabilityActor actor, string accountId, string scope, PermissionState state, CancellationToken ct = default)
		=> Gated(async () =>
		{
			if (PortalPermission.Canonical(scope) is not { } canonical)
				return Refuse<Success>(RoleRefusalKind.Invalid, $"Unknown permission '{scope}'.");
			if (canonical == PortalPermission.Administrator)
				return Refuse<Success>(RoleRefusalKind.Invalid, "administrator comes only from a role; it cannot be set on an account.");
			if (!Enum.IsDefined(state))
				return Refuse<Success>(RoleRefusalKind.Invalid, "Unknown permission state.");
			var me = await capabilities.GetContextAsync(actor, ct);
			if (Unauthorized(me) is { } refusal) return refusal;
			if (await TargetAsync(actor, me, accountId, ct) is not string target)
				return await TargetRefusalAsync(actor, me, accountId, ct);
			if (state == PermissionState.Allow && !resolver.Resolve(me).Contains(canonical))
				return Refuse<Success>(RoleRefusalKind.Forbidden, $"You cannot grant {canonical}, which you do not hold.");

			await registry.SetAccountOverrideAsync(target, canonical, state);
			await invalidator.InvalidateAsync(target, ct);
			return new Success();
		}, ct);

	private Task<RoleOutcome<Success>> ChangeAssignmentAsync(CapabilityActor actor, string accountId, string slug, bool assign, CancellationToken ct)
		=> Gated(async () =>
		{
			if (await registry.GetRoleAsync(slug, ct) is not SharpRole role)
				return Refuse<Success>(RoleRefusalKind.NotFound, $"No role named '{slug}'.");
			if (role.IsSystem)
				return Refuse<Success>(RoleRefusalKind.Invalid,
					$"{role.Name} is a system role; it follows a character's flags and cannot be {(assign ? "assigned" : "removed")} by hand.");
			var me = await capabilities.GetContextAsync(actor, ct);
			if (Unauthorized(me) is { } refusal) return refusal;
			if (!RoleHierarchy.Outranks(me, role.Priority)) return NotBelow<Success>(role, me);
			if (await TargetAsync(actor, me, accountId, ct) is not string target)
				return await TargetRefusalAsync(actor, me, accountId, ct);

			if (assign) await registry.AssignRoleToAccountAsync(target, role.Slug);
			else await registry.RemoveRoleFromAccountAsync(target, role.Slug);
			await invalidator.InvalidateAsync(target, ct);
			return new Success();
		}, ct);

	private async Task<RoleOutcome<SharpRole>> SaveCoreAsync(CapabilityActor actor, RoleDraft draft, CancellationToken ct)
	{
		var slug = draft.Slug.Trim();
		if (!SlugPattern().IsMatch(slug))
			return Refuse<SharpRole>(RoleRefusalKind.Invalid, "A role name is 1 to 32 lowercase letters, digits, '-' or '_'.");
		var color = string.IsNullOrWhiteSpace(draft.Color) ? null : draft.Color.Trim();
		if (color is not null && !ColorPattern().IsMatch(color))
			return Refuse<SharpRole>(RoleRefusalKind.Invalid, "A role colour is a hex colour such as #5aa9ff.");
		var permissions = new Dictionary<string, PermissionState>();
		foreach (var (scope, state) in draft.Permissions)
		{
			if (PortalPermission.Canonical(scope) is not { } canonical)
				return Refuse<SharpRole>(RoleRefusalKind.Invalid, $"Unknown permission '{scope}'.");
			if (!Enum.IsDefined(state))
				return Refuse<SharpRole>(RoleRefusalKind.Invalid, "Unknown permission state.");
			if (state != PermissionState.Inherit) permissions[canonical] = state;
		}

		var me = await capabilities.GetContextAsync(actor, ct);
		if (Unauthorized(me) is { } refusal) return refusal;
		var existing = await registry.GetRoleAsync(slug, ct) is SharpRole found ? found : null;
		if (existing is { IsSystem: true } && existing.Priority != draft.Priority)
			return Refuse<SharpRole>(RoleRefusalKind.Invalid, $"{existing.Name} is a system role and keeps priority {existing.Priority}.");
		if (existing is not null && !RoleHierarchy.Outranks(me, existing.Priority)) return NotBelow<SharpRole>(existing, me);
		if (!RoleHierarchy.Outranks(me, draft.Priority))
			return Refuse<SharpRole>(RoleRefusalKind.Forbidden,
				$"Priority {draft.Priority} is not below your highest role ({me.TopPriority}).");
		var unheld = RoleHierarchy.UnheldGrants(existing?.Permissions ?? new Dictionary<string, PermissionState>(), permissions, resolver.Resolve(me));
		if (unheld.Count > 0)
			return Refuse<SharpRole>(RoleRefusalKind.Forbidden, $"You cannot grant what you do not hold: {string.Join(", ", unheld)}.");

		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var name = draft.Name.Trim();
		var role = new SharpRole
		{
			Id = existing?.Id,
			Slug = slug,
			Name = name.Length > 0 ? name : existing?.Name ?? slug,
			Color = color,
			Priority = draft.Priority,
			IsSystem = existing?.IsSystem ?? false,
			Permissions = permissions,
			CreatedAt = existing?.CreatedAt ?? now,
			UpdatedAt = now
		};
		await registry.UpsertRoleAsync(role);
		foreach (var holder in await registry.GetAccountIdsForRoleAsync(role.Slug))
			await invalidator.InvalidateAsync(holder, ct);
		return role;
	}

	/// <summary>The target account's canonical id when the actor may change it, else null.</summary>
	private async Task<string?> TargetAsync(CapabilityActor actor, PermissionContext me, string accountId, CancellationToken ct)
	{
		var account = await accounts.GetByIdAsync(accountId, ct);
		if (account?.Id is null) return null;
		if (me.IsOwner) return account.Id;
		if (account.Id == actor.AccountId) return null;
		var target = await capabilities.GetContextAsync(new CapabilityActor(account.Id), ct);
		return target.IsOwner || !RoleHierarchy.Outranks(me, target.TopPriority) ? null : account.Id;
	}

	private async Task<RoleOutcome<Success>> TargetRefusalAsync(CapabilityActor actor, PermissionContext me, string accountId, CancellationToken ct)
	{
		var account = await accounts.GetByIdAsync(accountId, ct);
		if (account?.Id is null) return Refuse<Success>(RoleRefusalKind.NotFound, "No such account.");
		if (account.Id == actor.AccountId) return Refuse<Success>(RoleRefusalKind.Forbidden, "You cannot change your own roles or permissions.");
		return Refuse<Success>(RoleRefusalKind.Forbidden,
			$"{account.Username}'s highest role is not below yours ({me.TopPriority}).");
	}

	private RoleRefusal? Unauthorized(PermissionContext me)
		=> resolver.Resolve(me).Contains(PortalPermission.RolesAdmin)
			? null
			: new RoleRefusal(RoleRefusalKind.Forbidden, $"Changing roles needs the {PortalPermission.RolesAdmin} permission.");

	private static RoleOutcome<T> NotBelow<T>(SharpRole role, PermissionContext me)
		=> new RoleRefusal(RoleRefusalKind.Forbidden,
			$"{role.Name} (priority {role.Priority}) is not below your highest role ({me.TopPriority}).");

	private static RoleOutcome<T> Refuse<T>(RoleRefusalKind kind, string message) => new RoleRefusal(kind, message);

	private async Task<T> Gated<T>(Func<Task<T>> work, CancellationToken ct)
	{
		await _gate.WaitAsync(ct);
		try { return await work(); }
		finally { _gate.Release(); }
	}
}
