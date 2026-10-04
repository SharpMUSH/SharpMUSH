using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>
/// Who may manage which roles and accounts, by Discord's role-hierarchy rules: a manager acts only
/// below their own highest role, and grants only what they hold. RhostMUSH states the same rule for
/// its ranks ("lower level bits can not modify higher level bits"). The owner is exempt.
/// </summary>
public static class RoleHierarchy
{
	/// <summary>True when <paramref name="actor"/> sits strictly above <paramref name="priority"/>.</summary>
	public static bool Outranks(PermissionContext actor, int priority) => actor.IsOwner || priority < actor.TopPriority;

	/// <summary>
	/// The scopes <paramref name="proposed"/> newly allows (compared with <paramref name="current"/>)
	/// that <paramref name="granted"/> lacks. A manager cannot hand out a scope they do not hold.
	/// </summary>
	public static IReadOnlyList<string> UnheldGrants(
		IReadOnlyDictionary<string, PermissionState> current,
		IReadOnlyDictionary<string, PermissionState> proposed,
		IReadOnlySet<string> granted)
		=> PortalPermission.AllScopes
			.Where(scope => PermissionResolver.StateOf(proposed, scope) == PermissionState.Allow
				&& PermissionResolver.StateOf(current, scope) != PermissionState.Allow
				&& !granted.Contains(scope))
			.ToArray();

	/// <summary>The roles in <paramref name="roles"/> that are not <c>everyone</c>, highest priority first.</summary>
	public static IEnumerable<SharpRole> Ranked(IEnumerable<SharpRole> roles)
		=> roles.Where(r => !BuiltInRoles.IsEveryone(r)).OrderByDescending(r => r.Priority).ThenBy(r => r.Slug, StringComparer.Ordinal);
}
