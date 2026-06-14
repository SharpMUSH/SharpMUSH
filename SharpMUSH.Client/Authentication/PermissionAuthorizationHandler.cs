using Microsoft.AspNetCore.Authorization;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Authentication;

/// <summary>
/// Succeeds a <see cref="PermissionRequirement"/> when the caller carries a matching
/// <see cref="PortalPermission.ClaimType"/> claim for the requested scope.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
	protected override Task HandleRequirementAsync(
		AuthorizationHandlerContext context, PermissionRequirement requirement)
	{
		if (context.User.HasClaim(PortalPermission.ClaimType, requirement.Scope))
		{
			context.Succeed(requirement);
		}

		return Task.CompletedTask;
	}
}
