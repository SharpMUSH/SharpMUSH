using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Authentication;

/// <summary>
/// Synthesises an authorization policy on demand for any known <see cref="PortalPermission"/> scope,
/// so pages can use <c>[Authorize(Policy = "wiki.admin")]</c> without registering each policy by hand.
/// Unknown / default / fallback policy names delegate to the framework default provider.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
	private readonly DefaultAuthorizationPolicyProvider _fallback;

	public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
		=> _fallback = new DefaultAuthorizationPolicyProvider(options);

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
