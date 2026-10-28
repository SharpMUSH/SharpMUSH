using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Who may manage which roles and accounts, by Discord's role-hierarchy rules: a manager acts only
/// below their own highest role, and grants only what they hold. The owner is exempt.
/// </summary>
public static class RoleHierarchy
{
	/// <summary>True when <paramref name="actor"/> sits strictly above <paramref name="priority"/>.</summary>
	public static bool Outranks(PermissionContext actor, int priority) => actor.IsOwner || priority < actor.TopPriority;

	/// <summary>
	/// The scopes <paramref name="proposed"/> newly allows (compared with <paramref name="current"/>)
	/// that <paramref name="granted"/> lacks. A manager cannot hand out a scope they do not hold.
	/// Taking <see cref="PortalPermission.ProtectRole"/> off a role counts too, since only the owner guards a role.
	/// </summary>
	public static IReadOnlyList<string> UnheldGrants(
		IReadOnlyDictionary<string, PermissionState> current,
		IReadOnlyDictionary<string, PermissionState> proposed,
		IReadOnlySet<string> granted)
		=> PortalPermission.AllScopes.Concat(proposed.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(scope => ChangesGrant(current, proposed, scope) && !CanGrant(granted, scope))
			.ToArray();

	private static bool ChangesGrant(
		IReadOnlyDictionary<string, PermissionState> current,
		IReadOnlyDictionary<string, PermissionState> proposed,
		string scope)
		=> (PermissionResolver.StateOf(proposed, scope) == PermissionState.Allow)
			!= (PermissionResolver.StateOf(current, scope) == PermissionState.Allow)
			&& (PermissionResolver.StateOf(proposed, scope) == PermissionState.Allow || scope == PortalPermission.ProtectRole);

	/// <summary>
	/// Whether a manager holding <paramref name="granted"/> may allow <paramref name="scope"/>: they hold it,
	/// or it is an in-game scope or a custom permission and they are a wizard (in PennMUSH a wizard grants
	/// every power without holding any). Passing a role's guard (<see cref="PortalPermission.ControlRole"/>) is the
	/// exception: only a holder grants it, so a wizard cannot hand itself the pass of a role guarded against it.
	/// Guarding a role (<see cref="PortalPermission.ProtectRole"/>) is the owner's alone, told by the pass only the
	/// owner holds (god's), so no one can guard a role of their own against those above them.
	/// Callers check that a custom scope exists first.
	/// </summary>
	public static bool CanGrant(IReadOnlySet<string> granted, string scope)
		=> scope == PortalPermission.ProtectRole
			? granted.Contains(PortalPermission.ControlRole(BuiltInRoles.GodSlug))
			: granted.Contains(scope)
			|| (!PortalPermission.IsControlRole(scope)
				&& (PortalPermission.IsGameScope(scope) || !PortalPermission.IsKnown(scope))
				&& granted.Contains(PortalPermission.GameWizard));

	/// <summary>The roles in <paramref name="roles"/> that are not <c>everyone</c>, highest priority first.</summary>
	public static IEnumerable<SharpRole> Ranked(IEnumerable<SharpRole> roles)
		=> roles.Where(r => !BuiltInRoles.IsEveryone(r)).OrderByDescending(r => r.Priority).ThenBy(r => r.Slug, StringComparer.Ordinal);
}
