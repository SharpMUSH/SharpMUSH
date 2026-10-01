using System.Security.Claims;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Reads the caller's <see cref="PortalRole"/> from their claims principal, for a hierarchy check against
/// an application's minimum role. An account session carries one role claim; the development principal
/// leads with <c>Admin</c>, which is no <see cref="PortalRole"/>, and then claims every role. So the
/// answer is the highest recognized role claim, not the first claim.
/// </summary>
public static class PortalRoleHelper
{
	/// <summary>The caller's highest recognized role, or <see cref="PortalRole.Guest"/> when unauthenticated/unknown.</summary>
	public static PortalRole CurrentRole(ClaimsPrincipal? user) =>
		user?.FindAll(ClaimTypes.Role)
			.Select(claim => Enum.TryParse<PortalRole>(claim.Value, ignoreCase: true, out var role) ? role : PortalRole.Guest)
			.DefaultIfEmpty(PortalRole.Guest)
			.Max()
		?? PortalRole.Guest;

	/// <summary>True when the caller's role meets or exceeds <paramref name="minimum"/>.</summary>
	public static bool Meets(ClaimsPrincipal? user, PortalRole minimum) => CurrentRole(user) >= minimum;
}
