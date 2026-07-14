using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Client.Authentication;

/// <summary>
/// Production AuthenticationStateProvider backed by the account session
/// (replaces the never-configured OIDC wiring). Role and permission claims come
/// from the account-login response and drive [Authorize] / policy gates in the portal.
/// Server-side authorization is enforced independently per request.
/// </summary>
public class AccountAuthStateProvider : AuthenticationStateProvider
{
	private readonly IAccountAuthState _accountAuth;

	public AccountAuthStateProvider(IAccountAuthState accountAuth)
	{
		_accountAuth = accountAuth;
		_accountAuth.AuthStateChanged += () => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
	}

	public override Task<AuthenticationState> GetAuthenticationStateAsync()
	{
		if (!_accountAuth.IsLoggedIn)
			return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

		var claims = new List<Claim>
		{
			new(ClaimTypes.Name, _accountAuth.Username ?? "account"),
			new(ClaimTypes.Role, _accountAuth.Role ?? nameof(PortalRole.Guest)),
		};
		claims.AddRange(_accountAuth.Permissions.Select(p => new Claim(PortalPermission.ClaimType, p)));

		var identity = new ClaimsIdentity(claims, "AccountSession");
		return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
	}
}
