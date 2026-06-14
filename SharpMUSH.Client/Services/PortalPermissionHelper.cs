using System.Security.Claims;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the caller's granted <see cref="PortalPermission"/> scopes from their <c>perm</c> claims.
/// Mirrors <see cref="PortalRoleHelper"/> but checks per-scope permission grants rather than the
/// role hierarchy.
/// </summary>
public static class PortalPermissionHelper
{
	/// <summary>True when the caller holds the given permission <paramref name="scope"/>.</summary>
	public static bool Has(ClaimsPrincipal? user, string scope)
		=> user?.HasClaim(PortalPermission.ClaimType, scope) == true;
}
