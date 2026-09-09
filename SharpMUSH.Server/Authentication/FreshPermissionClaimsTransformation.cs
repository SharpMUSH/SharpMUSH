using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Authentication rebuilds permission claims from persisted account authority on every request.
/// This covers secondary checks inside actions and optional-authentication read endpoints, as
/// well as policy gates. Cached session claims remain a login optimization, never a grant cache.
/// </summary>
public sealed class FreshPermissionClaimsTransformation(IAdministrativeCapabilityService capabilities) : IClaimsTransformation
{
	public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
	{
		if (principal.Identity?.IsAuthenticated != true) return principal;
		var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
		if (id is null) return principal;
		var scopes = await capabilities.GetGrantedScopesAsync(new(id));
		// Clone so authentication schemes sharing an identity cannot observe mutable claims.
		var current = new ClaimsPrincipal(principal.Identities.Select(identity => new ClaimsIdentity(identity)));
		foreach (var identity in current.Identities)
			foreach (var claim in identity.FindAll(PortalPermission.ClaimType).ToArray()) identity.RemoveClaim(claim);
		if (current.Identity is ClaimsIdentity destination)
			destination.AddClaims(scopes.Select(scope => new Claim(PortalPermission.ClaimType, scope)));
		return current;
	}
}
