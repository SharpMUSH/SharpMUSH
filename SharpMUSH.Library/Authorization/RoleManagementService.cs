using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Library.Commands.Database;
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

/// <summary>
/// Who is making a role change: a signed-in account (from the portal, or a character played through
/// one), or a game object acting as itself, which holds what its <see cref="ObjectGrants"/> say whether
/// or not it has an account.
/// </summary>
public union RoleActor(CapabilityActor, AnySharpObject);

/// <summary>The editable fields of a role, as a create or an edit proposes them.</summary>
public sealed record RoleDraft(
	string Slug,
	string Name,
	string? Color,
	int Priority,
	IReadOnlyDictionary<string, PermissionState> Permissions);

/// <summary>
/// Every change to roles, assignments and overrides, from the portal and from the game (<c>@role</c>,
/// and <c>@set</c>/<c>@power</c> on WIZARD, ROYALTY and the powers), goes through here so all of them
/// apply the same rules (<see cref="RoleHierarchy"/>):
/// <list type="bullet">
/// <item>Every change needs <see cref="PortalPermission.RolesAdmin"/>.</item>
/// <item>A role can be created, edited, deleted, assigned or removed only when it sits below the
/// actor's highest role, and only an account or object whose highest role is below the actor's can
/// have its roles or overrides changed.</item>
/// <item>A role or override can allow only scopes the actor holds; a holder of
/// <see cref="PortalPermission.GameWizard"/> may also allow any in-game scope.</item>
/// <item>Nobody but the owner changes their own roles or overrides.</item>
/// <item>System roles keep their slug and priority and cannot be deleted. The implicit ones
/// (<c>everyone</c>, <c>player</c>, <c>god</c>) are never assigned.</item>
/// <item>An object may give a role it holds to a non-player object it owns, and take it back, without
/// <see cref="PortalPermission.RolesAdmin"/>: PennMUSH lets a wizard set WIZARD, and royalty set
/// ROYALTY, on their own things.</item>
/// </list>
/// The owner (player #1 and the account linked to it) is exempt from the hierarchy rules. Role order
/// decides only who may manage whom; it grants no control over objects.
/// </summary>
public interface IRoleManagementService
{
	/// <summary>Creates a role, or replaces an existing role's editable fields.</summary>
	Task<RoleOutcome<SharpRole>> SaveRoleAsync(RoleActor actor, RoleDraft draft, CancellationToken ct = default);

	/// <summary>Edits an existing role: <paramref name="change"/> turns the stored role into a draft.</summary>
	Task<RoleOutcome<SharpRole>> EditRoleAsync(RoleActor actor, string slug, Func<SharpRole, RoleDraft> change, CancellationToken ct = default);

	/// <summary>Deletes a role and every assignment of it.</summary>
	Task<RoleOutcome<Success>> DeleteRoleAsync(RoleActor actor, string slug, CancellationToken ct = default);

	/// <summary>Gives an account a role.</summary>
	Task<RoleOutcome<Success>> AssignAsync(RoleActor actor, string accountId, string slug, CancellationToken ct = default);

	/// <summary>Takes a role away from an account.</summary>
	Task<RoleOutcome<Success>> UnassignAsync(RoleActor actor, string accountId, string slug, CancellationToken ct = default);

	/// <summary>
	/// Sets per-account overrides, all or none: every scope is checked before any is written.
	/// <see cref="PermissionState.Inherit"/> clears them.
	/// </summary>
	Task<RoleOutcome<Success>> SetOverridesAsync(RoleActor actor, string accountId, IReadOnlyCollection<string> scopes, PermissionState state, CancellationToken ct = default);

	/// <summary>Gives a game object a role.</summary>
	Task<RoleOutcome<Success>> AssignToObjectAsync(RoleActor actor, AnySharpObject target, string slug, CancellationToken ct = default);

	/// <summary>Takes a role away from a game object.</summary>
	Task<RoleOutcome<Success>> UnassignFromObjectAsync(RoleActor actor, AnySharpObject target, string slug, CancellationToken ct = default);

	/// <summary>Sets overrides on a game object, all or none, as <see cref="SetOverridesAsync"/> does for accounts.</summary>
	Task<RoleOutcome<Success>> SetObjectOverridesAsync(RoleActor actor, AnySharpObject target, IReadOnlyCollection<string> scopes, PermissionState state, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed partial class RoleManagementService(
	IRoleRegistryService registry,
	IAccountService accounts,
	IAdministrativeCapabilityService capabilities,
	IPermissionResolver resolver,
	IAccountClaimsInvalidator invalidator,
	IMediator mediator) : IRoleManagementService
{
	/// <summary>Validation and the write it guards run one at a time, so two changes cannot both pass a stale check.</summary>
	private readonly SemaphoreSlim _gate = new(1, 1);

	[GeneratedRegex("^[a-z0-9_-]{1,32}$")]
	private static partial Regex SlugPattern();

	[GeneratedRegex("^#[0-9a-fA-F]{6}$")]
	private static partial Regex ColorPattern();

	public Task<RoleOutcome<SharpRole>> SaveRoleAsync(RoleActor actor, RoleDraft draft, CancellationToken ct = default)
		=> Gated(async () => await SaveCoreAsync(actor, draft, ct), ct);

	public Task<RoleOutcome<SharpRole>> EditRoleAsync(RoleActor actor, string slug, Func<SharpRole, RoleDraft> change, CancellationToken ct = default)
		=> Gated(async () => await registry.GetRoleAsync(slug, ct) switch
		{
			SharpRole role => await SaveCoreAsync(actor, change(role) with { Slug = role.Slug }, ct),
			_ => Refuse<SharpRole>(RoleRefusalKind.NotFound, $"No role named '{slug}'.")
		}, ct);

	public Task<RoleOutcome<Success>> DeleteRoleAsync(RoleActor actor, string slug, CancellationToken ct = default)
		=> Gated(async () =>
		{
			if (await registry.GetRoleAsync(slug, ct) is not SharpRole role)
				return Refuse<Success>(RoleRefusalKind.NotFound, $"No role named '{slug}'.");
			if (role.IsSystem)
				return Refuse<Success>(RoleRefusalKind.Invalid, $"{role.Name} is a system role and cannot be deleted.");
			var me = (await ActorGrantsAsync(actor, ct)).Context;
			if (Unauthorized(me) is { } refusal) return refusal;
			if (!RoleHierarchy.Outranks(me, role.Priority)) return NotBelow<Success>(role, me);

			var holders = await registry.GetAccountIdsForRoleAsync(role.Slug);
			foreach (var holder in holders)
				await registry.RemoveRoleFromAccountAsync(holder, role.Slug);
			foreach (var number in await registry.GetObjectsForRoleAsync(role.Slug, ct))
				await registry.RemoveRoleFromObjectAsync(number, role.Slug);
			await registry.RemoveRoleAsync(role.Slug);
			foreach (var holder in holders)
				await invalidator.InvalidateAsync(holder, ct);
			await mediator.Send(new InvalidateGrantsCommand(null), ct);
			return new Success();
		}, ct);

	public Task<RoleOutcome<Success>> AssignAsync(RoleActor actor, string accountId, string slug, CancellationToken ct = default)
		=> ChangeAssignmentAsync(actor, accountId, slug, assign: true, ct);

	public Task<RoleOutcome<Success>> UnassignAsync(RoleActor actor, string accountId, string slug, CancellationToken ct = default)
		=> ChangeAssignmentAsync(actor, accountId, slug, assign: false, ct);

	public Task<RoleOutcome<Success>> SetOverridesAsync(RoleActor actor, string accountId, IReadOnlyCollection<string> scopes, PermissionState state, CancellationToken ct = default)
		=> Gated(async () =>
		{
			if (OverrideScopes(scopes, state) is not List<string> canonical)
				return OverrideRefusal(scopes, state);

			var me = (await ActorGrantsAsync(actor, ct)).Context;
			if (Unauthorized(me) is { } refusal) return refusal;
			if (await TargetAsync(actor, me, accountId, ct) is not string target)
				return await TargetRefusalAsync(actor, me, accountId, ct);
			if (state == PermissionState.Allow && Unheld(me, canonical) is { } unheld) return unheld;

			foreach (var scope in canonical.Distinct())
				await registry.SetAccountOverrideAsync(target, scope, state);
			await invalidator.InvalidateAsync(target, ct);
			return new Success();
		}, ct);

	private Task<RoleOutcome<Success>> ChangeAssignmentAsync(RoleActor actor, string accountId, string slug, bool assign, CancellationToken ct)
		=> Gated(async () =>
		{
			if (await registry.GetRoleAsync(slug, ct) is not SharpRole role)
				return Refuse<Success>(RoleRefusalKind.NotFound, $"No role named '{slug}'.");
			if (BuiltInRoles.IsImplicit(role.Slug))
				return ImplicitRole(role);
			var me = (await ActorGrantsAsync(actor, ct)).Context;
			if (Unauthorized(me) is { } refusal) return refusal;
			if (!RoleHierarchy.Outranks(me, role.Priority)) return NotBelow<Success>(role, me);
			if (await TargetAsync(actor, me, accountId, ct) is not string target)
				return await TargetRefusalAsync(actor, me, accountId, ct);
			if (assign) await registry.AssignRoleToAccountAsync(target, role.Slug);
			else await registry.RemoveRoleFromAccountAsync(target, role.Slug);
			await invalidator.InvalidateAsync(target, ct);
			return new Success();
		}, ct);

	private static RoleOutcome<Success> ImplicitRole(SharpRole role)
		=> Refuse<Success>(RoleRefusalKind.Invalid, role.Slug switch
		{
			BuiltInRoles.EveryoneSlug => $"{role.Name} is held by everyone and is never assigned.",
			BuiltInRoles.PlayerSlug => $"{role.Name} is held by every player character that is not a guest and is never assigned.",
			_ => $"{role.Name} is held by player #1 alone and is never assigned."
		});

	public Task<RoleOutcome<Success>> AssignToObjectAsync(RoleActor actor, AnySharpObject target, string slug, CancellationToken ct = default)
		=> ChangeObjectAssignmentAsync(actor, target, slug, assign: true, ct);

	public Task<RoleOutcome<Success>> UnassignFromObjectAsync(RoleActor actor, AnySharpObject target, string slug, CancellationToken ct = default)
		=> ChangeObjectAssignmentAsync(actor, target, slug, assign: false, ct);

	private Task<RoleOutcome<Success>> ChangeObjectAssignmentAsync(RoleActor actor, AnySharpObject target, string slug, bool assign, CancellationToken ct)
		=> Gated(async () =>
		{
			if (await registry.GetRoleAsync(slug, ct) is not SharpRole role)
				return Refuse<Success>(RoleRefusalKind.NotFound, $"No role named '{slug}'.");
			if (BuiltInRoles.IsImplicit(role.Slug))
				return ImplicitRole(role);
			var me = await ActorGrantsAsync(actor, ct);
			if (!await SharesWithOwnThingAsync(actor, me, target, role, ct))
			{
				if (Unauthorized(me.Context) is { } refusal) return refusal;
				if (!RoleHierarchy.Outranks(me.Context, role.Priority)) return NotBelow<Success>(role, me.Context);
				var ownPower = GamePowers.ForRole(role.Slug) is not null && IsWizardActingOnItself(actor, me.Context, target);
				if (!ownPower && await ObjectTargetRefusalAsync(actor, me.Context, target, ct) is { } targetRefusal) return targetRefusal;
			}

			var number = target.Object().Key;
			if (assign) await registry.AssignRoleToObjectAsync(number, role.Slug);
			else await registry.RemoveRoleFromObjectAsync(number, role.Slug);
			await ObjectChangedAsync(target, ct);
			return new Success();
		}, ct);

	public Task<RoleOutcome<Success>> SetObjectOverridesAsync(RoleActor actor, AnySharpObject target, IReadOnlyCollection<string> scopes, PermissionState state, CancellationToken ct = default)
		=> Gated(async () =>
		{
			if (OverrideScopes(scopes, state) is not List<string> canonical)
				return OverrideRefusal(scopes, state);

			var me = (await ActorGrantsAsync(actor, ct)).Context;
			if (Unauthorized(me) is { } refusal) return refusal;
			var ownPowers = canonical.All(scope => GamePowers.ForScope(scope) is not null) && IsWizardActingOnItself(actor, me, target);
			if (!ownPowers && await ObjectTargetRefusalAsync(actor, me, target, ct) is { } targetRefusal) return targetRefusal;
			if (state == PermissionState.Allow && Unheld(me, canonical) is { } unheld) return unheld;

			foreach (var scope in canonical.Distinct())
				await registry.SetObjectOverrideAsync(target.Object().Key, scope, state);
			await ObjectChangedAsync(target, ct);
			return new Success();
		}, ct);

	/// <summary>What the actor holds, read fresh: a game object's own grants, or the account's context.</summary>
	private async Task<ObjectGrants> ActorGrantsAsync(RoleActor actor, CancellationToken ct) => actor switch
	{
		AnySharpObject obj => await capabilities.GetObjectGrantsAsync(obj, ct),
		CapabilityActor account => ObjectGrants.FromContext(await capabilities.GetContextAsync(account, ct)),
	};

	/// <summary>The account acting, or the one the acting character is linked to.</summary>
	private async Task<string?> ActorAccountIdAsync(RoleActor actor, CancellationToken ct) => actor switch
	{
		CapabilityActor account => account.AccountId,
		AnySharpObject { IsPlayer: true } obj => (await accounts.GetAccountForCharacterAsync(obj.Object().DBRef, ct))?.Id,
		_ => null
	};

	/// <summary>The game object acting, when the actor is one or is playing one.</summary>
	private static DBRef? ActingObject(RoleActor actor) => actor switch
	{
		AnySharpObject obj => obj.Object().DBRef,
		CapabilityActor account => account.Executor,
	};

	/// <summary>
	/// PennMUSH's own-thing rule: an object may give a role it holds itself to, or take it from, a
	/// non-player object it owns.
	/// </summary>
	private static async Task<bool> SharesWithOwnThingAsync(RoleActor actor, ObjectGrants me, AnySharpObject target, SharpRole role, CancellationToken ct)
	{
		if (target.IsPlayer || ActingObject(actor) is not { } acting || !me.HoldsRole(role.Slug)) return false;
		var owner = await target.Object().Owner.WithCancellation(ct);
		return owner.Object.DBRef.Number == acting.Number;
	}

	/// <summary>
	/// A <see cref="PortalPermission.GameWizard"/> holder changing itself. As in PennMUSH a wizard may
	/// <c>@power</c> itself, so this lets it set its own power overrides and power roles (Builder, Guest).
	/// </summary>
	private bool IsWizardActingOnItself(RoleActor actor, PermissionContext me, AnySharpObject target)
		=> ActingObject(actor) is { } acting && acting.Number == target.Object().Key
			&& resolver.Resolve(me).Contains(PortalPermission.GameWizard);

	/// <summary>Why the actor may not change <paramref name="target"/>'s roles or overrides, or null when it may.</summary>
	private async Task<RoleOutcome<Success>?> ObjectTargetRefusalAsync(RoleActor actor, PermissionContext me, AnySharpObject target, CancellationToken ct)
	{
		if (me.IsOwner) return null;
		if (ActingObject(actor) is { } acting && acting.Number == target.Object().Key)
			return Refuse<Success>(RoleRefusalKind.Forbidden, "You cannot change your own roles or permissions.");
		var theirs = (await capabilities.GetObjectGrantsAsync(target, ct)).Context;
		return theirs.IsOwner || !RoleHierarchy.Outranks(me, theirs.TopPriority)
			? Refuse<Success>(RoleRefusalKind.Forbidden, $"{target.Object().Name}'s highest role is not below yours ({me.TopPriority}).")
			: null;
	}

	/// <summary>Expires the object's cached grants and, for a linked character, its account's portal claims.</summary>
	private async Task ObjectChangedAsync(AnySharpObject target, CancellationToken ct)
	{
		await mediator.Send(new InvalidateGrantsCommand(target.Object().Key), ct);
		if (target.IsPlayer && await accounts.GetAccountForCharacterAsync(target.Object().DBRef, ct) is { Id: { } accountId })
			await invalidator.InvalidateAsync(accountId, ct);
	}

	/// <summary>The catalog spelling of each scope when every one may be overridden, else null.</summary>
	private static List<string>? OverrideScopes(IReadOnlyCollection<string> scopes, PermissionState state)
	{
		if (scopes.Count == 0 || !Enum.IsDefined(state)) return null;
		var canonical = new List<string>();
		foreach (var scope in scopes)
		{
			if (PortalPermission.Canonical(scope) is not { } known || known == PortalPermission.Administrator) return null;
			canonical.Add(known);
		}

		return canonical;
	}

	/// <summary>Why <see cref="OverrideScopes"/> refused.</summary>
	private static RoleOutcome<Success> OverrideRefusal(IReadOnlyCollection<string> scopes, PermissionState state)
	{
		if (scopes.Count == 0)
			return Refuse<Success>(RoleRefusalKind.Invalid, "Name at least one permission.");
		if (!Enum.IsDefined(state))
			return Refuse<Success>(RoleRefusalKind.Invalid, "Unknown permission state.");
		var bad = scopes.First(scope => PortalPermission.Canonical(scope) is not { } known || known == PortalPermission.Administrator);
		return PortalPermission.Canonical(bad) is null
			? Refuse<Success>(RoleRefusalKind.Invalid, $"Unknown permission '{bad}'.")
			: Refuse<Success>(RoleRefusalKind.Invalid, "administrator comes only from a role; it cannot be set as an override.");
	}

	/// <summary>The refusal for allowing scopes the actor may not grant, or null when it may grant them all.</summary>
	private RoleOutcome<Success>? Unheld(PermissionContext me, IEnumerable<string> scopes)
	{
		var held = resolver.Resolve(me);
		var unheld = scopes.Where(scope => !RoleHierarchy.CanGrant(held, scope)).ToArray();
		return unheld.Length > 0
			? Refuse<Success>(RoleRefusalKind.Forbidden, $"You cannot grant what you do not hold: {string.Join(", ", unheld)}.")
			: null;
	}

	private async Task<RoleOutcome<SharpRole>> SaveCoreAsync(RoleActor actor, RoleDraft draft, CancellationToken ct)
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

		var me = (await ActorGrantsAsync(actor, ct)).Context;
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
		await mediator.Send(new InvalidateGrantsCommand(null), ct);
		return role;
	}

	/// <summary>The target account's canonical id when the actor may change it, else null.</summary>
	private async Task<string?> TargetAsync(RoleActor actor, PermissionContext me, string accountId, CancellationToken ct)
	{
		var account = await accounts.GetByIdAsync(accountId, ct);
		if (account?.Id is null) return null;
		if (me.IsOwner) return account.Id;
		if (account.Id == await ActorAccountIdAsync(actor, ct)) return null;
		var target = await capabilities.GetContextAsync(new CapabilityActor(account.Id), ct);
		return target.IsOwner || !RoleHierarchy.Outranks(me, target.TopPriority) ? null : account.Id;
	}

	private async Task<RoleOutcome<Success>> TargetRefusalAsync(RoleActor actor, PermissionContext me, string accountId, CancellationToken ct)
	{
		var account = await accounts.GetByIdAsync(accountId, ct);
		if (account?.Id is null) return Refuse<Success>(RoleRefusalKind.NotFound, "No such account.");
		if (account.Id == await ActorAccountIdAsync(actor, ct)) return Refuse<Success>(RoleRefusalKind.Forbidden, "You cannot change your own roles or permissions.");
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
