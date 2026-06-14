using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Resolves <c>[Authorize(Policy = PortalPermission.X)]</c> policy names on demand: any known
/// permission scope (<see cref="PortalPermission.IsKnown"/>) is materialized into a policy that
/// requires an authenticated user carrying the matching <c>perm</c> claim. All other policy names
/// (and the default/fallback policies) are delegated to the standard
/// <see cref="DefaultAuthorizationPolicyProvider"/>.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
	private readonly DefaultAuthorizationPolicyProvider _fallback;

	public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
	{
		_fallback = new DefaultAuthorizationPolicyProvider(options);
	}

	public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
	{
		if (PortalPermission.IsKnown(policyName))
		{
			var policy = new AuthorizationPolicyBuilder()
				.RequireAuthenticatedUser()
				.AddRequirements(new PermissionRequirement(policyName))
				.Build();
			return Task.FromResult<AuthorizationPolicy?>(policy);
		}

		return _fallback.GetPolicyAsync(policyName);
	}

	public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

	public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
