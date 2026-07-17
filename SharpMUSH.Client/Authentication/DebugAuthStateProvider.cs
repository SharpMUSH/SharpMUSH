using Microsoft.AspNetCore.Components.Authorization;
using System.Linq;
using System.Security.Claims;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

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
/// <see cref="AccountAuthService.GetDebugOttAsync"/> itself single-flights and caches the result
/// for the app lifetime once it succeeds, so repeated <see cref="GetAuthenticationStateAsync"/>
/// calls here — even concurrent ones from other components — do not hammer the endpoint or mint
/// redundant single-use tokens; this provider no longer keeps its own cache.
///
/// Returns an anonymous principal — no fabricated claims — when the server is not yet
/// reachable or the bootstrap account does not yet exist. <c>ServerStartupGate</c> (see
/// App.razor) keeps this provider from ever being queried before the server is actually up,
/// so this path should only be hit by a very late/unlucky bootstrap race, and it must not
/// paper over that with a fake "DebugAdmin" identity.
/// </summary>
public class DebugAuthStateProvider : AuthenticationStateProvider
{
	private readonly IAccountAuthState _accountAuth;

	/// <summary>One role claim per <see cref="PortalRole"/> name PLUS one permission claim per
	/// scope. The bootstrap account owns player #1 (God), the top of the hierarchy; role and
	/// permission checks are exact matches, so the debug principal gets every role and every
	/// permission scope (so both the legacy role gates and the new policy gates authorize).</summary>
	private static readonly Claim[] DebugRoleClaims =
	[
		.. Enum.GetNames<PortalRole>().Select(name => new Claim(ClaimTypes.Role, name)),
		.. PortalPermission.AllScopes.Select(scope => new Claim(PortalPermission.ClaimType, scope))
	];

	public DebugAuthStateProvider(IAccountAuthState accountAuth)
	{
		_accountAuth = accountAuth;
		// So every AuthorizeView (nav items, the sidebar's bottom-left account card/panel) flips live
		// the instant AccountAuthService raises AuthStateChanged (login/register/setup/logout) instead
		// of waiting for the next unrelated re-render.
		_accountAuth.AuthStateChanged += HandleAccountAuthStateChanged;
	}

	private void HandleAccountAuthStateChanged() =>
		// AccountAuthService.LogoutAsync clears its own cached debug-OTT task on the logged-out
		// transition, so a later re-login in this tab re-fetches from the server rather than
		// resurrecting the pre-logout OTT response — nothing to drop here anymore.
		NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

	public override async Task<AuthenticationState> GetAuthenticationStateAsync()
	{
		// Hydrate first: this method is called on every auth-state query, including the very first
		// one from CascadingAuthenticationState (App.razor root) — which can fire before MainLayout
		// ever calls InitAsync, e.g. on a page refresh where component init order isn't guaranteed.
		// Without this, ExplicitlyLoggedOut below would still read the un-hydrated default `false`
		// and a real logout wouldn't survive the reload.
		await _accountAuth.InitAsync();

		// Explicit-logout latch: enforced again here (belt-and-braces alongside the chokepoint in
		// AccountAuthService.GetDebugOttAsync) because this method is the one actually called on
		// every auth-state query. Once latched, no cached OTT reuse and no static-sentinel fallback
		// claims — an anonymous principal is the only correct answer until the next real login.
		if (_accountAuth.ExplicitlyLoggedOut)
			return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

		// No cache here: AccountAuthService.GetDebugOttAsync() single-flights and caches the
		// successful response itself, so this just delegates every time.
		var debugOtt = await _accountAuth.GetDebugOttAsync();

		if (debugOtt?.AccountId is null)
		{
			// Server not yet reachable or the bootstrap account does not exist yet — anonymous,
			// not a fabricated identity. ServerStartupGate should have prevented this provider
			// from ever being queried this early; if it still happens, no fake claims either way.
			return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
		}

		List<Claim> claims =
		[
			new(ClaimTypes.NameIdentifier, debugOtt.AccountId),
			new(ClaimTypes.Name, debugOtt.AccountUsername ?? "admin"),
			new(ClaimTypes.Role, "Admin"),
			// The bootstrap account owns player #1 (God) — the top of the role
			// hierarchy. Role checks are EXACT string matches, so [Authorize(Roles="God")]
			// and [Authorize(Roles="Wizard")] each need their own claim. Grant every
			// PortalRole in dev. Mirrors the server-side DebugAuthenticationHandler.
			.. DebugRoleClaims,
			// Match the custom claims emitted by AccountSessionAuthenticationHandler /
			// DebugAuthenticationHandler so component logic that inspects character_key or
			// character_name works.
			new("character_key", "1"),
			new("character_name", debugOtt.PlayerName),
		];

		var identity = new ClaimsIdentity(claims, "DebugAuth");
		return new AuthenticationState(new ClaimsPrincipal(identity));
	}
}
