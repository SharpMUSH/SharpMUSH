using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Authorization;

/// <summary>Keep the persisted God role usable for recovery under every possible assignment.</summary>
public static class RoleRecoveryPolicy
{
	public static bool PreservesRecovery(SharpRole proposed, IEnumerable<SharpRole> existing)
	{
		var roles = existing.Where(r => r.Slug != proposed.Slug).Append(proposed).ToArray();
		var god = roles.SingleOrDefault(r => r.Slug == "god");
		if (god is null) return false;
		var resolver = new PermissionResolver();
		if (!resolver.Resolve([god]).Contains(PortalPermission.RolesAdmin)) return false;
		// A role need not be assigned today to become a lockout after a later assignment.
		return roles.All(role => resolver.Resolve([god, role]).Contains(PortalPermission.RolesAdmin));
	}
}
