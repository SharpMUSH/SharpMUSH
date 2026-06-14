using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Authentication;

/// <summary>
/// Development-only Blazor auth state provider.
///
/// On first call it hits the server's <c>/api/auth/debug-ott</c> endpoint to obtain the
/// real account session for the bootstrap account (the one linked to player #1 by
/// <c>BootstrapService</c>).  That call also populates
/// <see cref="AccountAuthService.AccountSessionToken"/> so that
/// <see cref="AccountAuthService.IsLoggedIn"/> returns <see langword="true"/> and all
/// downstream services (terminal auto-connect, character list, etc.) behave as if a real
/// login occurred.
///
/// The result is cached in memory for the lifetime of this instance so that repeated
/// <see cref="GetAuthenticationStateAsync"/> calls do not hammer the endpoint.
///
/// Falls back to static placeholder claims when the server is not yet reachable or
/// the bootstrap account does not yet exist (e.g. very first startup race).
/// </summary>
public class DebugAuthStateProvider(AccountAuthService accountAuth) : AuthenticationStateProvider
{
	/// Cached on first successful server round-trip.
	private AccountAuthService.DebugOttResponse? _cached;

	public override async Task<AuthenticationState> GetAuthenticationStateAsync()
	{
		if (_cached is null)
			_cached = await accountAuth.GetDebugOttAsync();

		List<Claim> claims;

		if (_cached?.AccountId is not null)
		{
			claims =
			[
				new(ClaimTypes.NameIdentifier, _cached.AccountId),
				new(ClaimTypes.Name, _cached.AccountUsername ?? "admin"),
				new(ClaimTypes.Role, "Admin"),
				// The admin pages gate on [Authorize(Roles = "Wizard")]; the bootstrap
				// account is the top-level admin, so grant Wizard in dev. Mirrors the
				// server-side DebugAuthenticationHandler, which emits both roles.
				new(ClaimTypes.Role, "Wizard"),
				// Match the custom claims emitted by JwtService / DebugAuthenticationHandler
				// so component logic that inspects character_key or character_name works.
				new("character_key", "1"),
				new("character_name", _cached.PlayerName),
			];
		}
		else
		{
			// Server not yet ready or bootstrap has not run — use static sentinel values.
			// Components that rely on a real account ID must handle "not found" gracefully.
			claims =
			[
				new(ClaimTypes.Name, "DebugAdmin"),
				new(ClaimTypes.NameIdentifier, "debug-bootstrap-pending"),
				new(ClaimTypes.Role, "Admin"),
				new(ClaimTypes.Role, "Wizard"),
			];
		}

		var identity = new ClaimsIdentity(claims, "DebugAuth");
		return new AuthenticationState(new ClaimsPrincipal(identity));
	}
}
