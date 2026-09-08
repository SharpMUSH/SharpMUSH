using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Authentication;

/// <summary>Rechecks persisted roles for every HTTP or SignalR policy gate; token grants are UI hints.</summary>
public sealed class PermissionAuthorizationHandler(IAdministrativeCapabilityService capabilities)
	: AuthorizationHandler<PermissionRequirement>
{
	protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
	{
		var accountId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
		if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(accountId)) return;
		DBRef? active = null;
		var claim = context.User.FindFirstValue(GameHub.CharacterDbrefClaim);
		if (claim is not null && !DBRef.TryParse(claim, out active)) return;
		if (await capabilities.AuthorizeAsync(new(accountId, active, active), requirement.Scope))
			context.Succeed(requirement);
	}
}
