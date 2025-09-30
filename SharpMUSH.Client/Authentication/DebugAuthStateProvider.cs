using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace SharpMUSH.Client.Authentication;

public class DebugAuthStateProvider : AuthenticationStateProvider
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
