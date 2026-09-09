using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
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
		// Portal HTTP authority is account-wide even while a character is selected.
		// Only a game hub invocation uses the active-character authority boundary.
		var claim = context.Resource is HubInvocationContext
			? context.User.FindFirstValue(GameHub.CharacterDbrefClaim) : null;
		if (context.Resource is HubInvocationContext && claim is null) return;
		if (claim is not null && !DBRef.TryParse(claim, out active)) return;
		if (await capabilities.AuthorizeAsync(new(accountId, active, active), requirement.Scope))
			context.Succeed(requirement);
	}
}
