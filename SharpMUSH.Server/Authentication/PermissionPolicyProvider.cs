using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Resolves <c>[Authorize(Policy = PortalPermission.X)]</c> policy names on demand: any known
/// permission scope (<see cref="PortalPermission.IsKnown"/>) is materialized into a policy that
/// requires an authenticated user carrying the matching <c>perm</c> claim, and a <see cref="DelegatedPolicies"/> name
/// into the policy of an endpoint apps may call. All other policy names
/// (and the default/fallback policies) are delegated to the standard
/// <see cref="DefaultAuthorizationPolicyProvider"/>.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
	private readonly DefaultAuthorizationPolicyProvider _fallback;
	private readonly IOptions<AuthenticationOptions> _authentication;

	public PermissionPolicyProvider(IOptions<AuthorizationOptions> options, IOptions<AuthenticationOptions> authentication)
	{
		_fallback = new DefaultAuthorizationPolicyProvider(options);
		_authentication = authentication;
	}

	public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
	{
		if (PortalPermission.IsKnown(policyName))
		{
			// An app's token reaches a permission's endpoint when its scopes cover the permission (PermissionAuthorizationHandler).
			var policy = new AuthorizationPolicyBuilder(DelegatedPolicies.Schemes(_authentication.Value))
				.RequireAuthenticatedUser()
				.AddRequirements(new PermissionRequirement(policyName))
				.Build();
			return Task.FromResult<AuthorizationPolicy?>(policy);
		}

		if (DelegatedPolicies.Build(policyName, _authentication.Value) is { } delegated)
			return Task.FromResult<AuthorizationPolicy?>(delegated);

		return _fallback.GetPolicyAsync(policyName);
	}

	public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

	public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
