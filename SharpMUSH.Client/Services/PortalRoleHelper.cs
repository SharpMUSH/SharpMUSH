using System.Security.Claims;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the caller's <see cref="PortalRole"/> from their JWT claims. The token carries a single
/// (highest) role claim (see JwtService), so comparison against an application's minimum role is a
/// straightforward hierarchy check.
/// </summary>
public static class PortalRoleHelper
{
	/// <summary>The caller's role, or <see cref="PortalRole.Guest"/> when unauthenticated/unknown.</summary>
	public static PortalRole CurrentRole(ClaimsPrincipal? user)
	{
		var claim = user?.FindFirst(ClaimTypes.Role)?.Value;
		return claim is not null && Enum.TryParse<PortalRole>(claim, ignoreCase: true, out var role)
			? role
			: PortalRole.Guest;
	}

	/// <summary>True when the caller's role meets or exceeds <paramref name="minimum"/>.</summary>
	public static bool Meets(ClaimsPrincipal? user, PortalRole minimum) => CurrentRole(user) >= minimum;
}
