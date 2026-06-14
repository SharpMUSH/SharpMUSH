using Microsoft.AspNetCore.Authorization;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// Succeeds a <see cref="PermissionRequirement"/> when the authenticated user carries a
/// <c>perm</c> claim whose value matches the required permission scope.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
	protected override Task HandleRequirementAsync(
		AuthorizationHandlerContext context,
		PermissionRequirement requirement)
	{
		if (context.User.HasClaim(PortalPermission.ClaimType, requirement.Scope))
		{
			context.Succeed(requirement);
		}

		return Task.CompletedTask;
	}
}
