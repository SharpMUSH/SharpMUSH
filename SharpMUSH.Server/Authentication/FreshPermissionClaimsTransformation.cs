using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Authentication rebuilds permission claims from persisted account authority on every request.
/// This covers secondary checks inside actions and optional-authentication read endpoints, as
/// well as policy gates. Cached session claims remain a login optimization, never a grant cache.
/// </summary>
/// <remarks>
/// <see cref="IClaimsTransformation"/> runs on every successful authenticate call, and one request
/// can make several (the default scheme, a policy's schemes, an explicit
/// <c>HttpContext.AuthenticateAsync</c>). The granted scopes are read once per request and account
/// and kept in <see cref="HttpContext.Items"/>, so the later calls reuse them; the next request
/// reads them again.
/// </remarks>
public sealed class FreshPermissionClaimsTransformation(
	IAdministrativeCapabilityService capabilities,
	IHttpContextAccessor httpContextAccessor) : IClaimsTransformation
{
	private static readonly object ItemsKey = new();

	public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
	{
		if (principal.Identity?.IsAuthenticated != true) return principal;
		var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
		if (id is null) return principal;
		var scopes = await GrantedScopesAsync(id);
		// Clone so authentication schemes sharing an identity cannot observe mutable claims.
		var current = new ClaimsPrincipal(principal.Identities.Select(identity => new ClaimsIdentity(identity)));
		foreach (var identity in current.Identities)
			foreach (var claim in identity.FindAll(PortalPermission.ClaimType).ToArray()) identity.RemoveClaim(claim);
		if (current.Identity is ClaimsIdentity destination)
			destination.AddClaims(scopes.Select(scope => new Claim(PortalPermission.ClaimType, scope)));
		return current;
	}

	private async Task<IReadOnlySet<string>> GrantedScopesAsync(string accountId)
	{
		var items = httpContextAccessor.HttpContext?.Items;
		if (items is null) return await capabilities.GetGrantedScopesAsync(new(accountId));
		if (items.TryGetValue(ItemsKey, out var memo) && memo is GrantedScopes known && known.AccountId == accountId)
			return known.Scopes;
		var scopes = await capabilities.GetGrantedScopesAsync(new(accountId));
		items[ItemsKey] = new GrantedScopes(accountId, scopes);
		return scopes;
	}

	private sealed record GrantedScopes(string AccountId, IReadOnlySet<string> Scopes);
}
