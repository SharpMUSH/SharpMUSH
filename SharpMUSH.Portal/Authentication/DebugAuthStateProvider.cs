using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace SharpMUSH.Portal.Authentication;

public class DebugAuthStateProvicer : AuthenticationStateProvider
{
	public override async Task<AuthenticationState> GetAuthenticationStateAsync()
	{
		await ValueTask.CompletedTask;

		var claims = new List<Claim>
		{
			new(ClaimTypes.Name, "DebugAdmin"),
			new(ClaimTypes.Role, "Admin")
		};

		var identity = new ClaimsIdentity(claims, "Server authentication");

		return new AuthenticationState(new ClaimsPrincipal(identity));
	}
}
