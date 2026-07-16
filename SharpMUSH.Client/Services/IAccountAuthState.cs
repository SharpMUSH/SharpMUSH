namespace SharpMUSH.Client.Services;

/// <summary>
/// Narrow read-only view over the account session, exposed so
/// <c>AccountAuthStateProvider</c> can depend on an interface instead of the full
/// <see cref="AccountAuthService"/> surface (and so tests can fake it without HTTP/JS).
/// </summary>
public interface IAccountAuthState
{
	bool IsLoggedIn { get; }

	/// <summary>
	/// The current account-session token, or <c>null</c> when not logged in. Read live (never cached
	/// downstream) by consumers such as <c>GameHubConnectionFactory</c> so a session established or
	/// cleared after they were constructed is still reflected on the next read.
	/// </summary>
	string? AccountSessionToken { get; }

	string? Username { get; }
	string? Role { get; }
	IReadOnlyList<string> Permissions { get; }

	/// <summary>
	/// True once the user has explicitly logged out in this tab (sessionStorage-latched).
	/// <c>SharpMUSH.Client.Authentication.DebugAuthStateProvider</c> must treat this as an
	/// anonymous session — no cached debug identity reuse — until the next successful
	/// login/register/setup clears it.
	/// </summary>
	bool ExplicitlyLoggedOut { get; }

	/// <summary>Raised whenever login/logout changes the session.</summary>
	event Action? AuthStateChanged;

	/// <summary>
	/// Single-flight, idempotent hydration from storage. Must be safe to call from any auth-state
	/// query (concurrently or repeatedly) before consulting <see cref="IsLoggedIn"/>,
	/// <see cref="ExplicitlyLoggedOut"/>, or any other state populated from storage — callers can't
	/// assume some other component already called it first (e.g. on a page refresh,
	/// CascadingAuthenticationState can query auth state before MainLayout runs its own init).
	/// </summary>
	Task InitAsync();

	/// <summary>
	/// Development-only: get a debug OTT for player #1 without credentials. Exposed on the
	/// narrow interface (rather than requiring the concrete <see cref="AccountAuthService"/>)
	/// so <c>SharpMUSH.Client.Authentication.DebugAuthStateProvider</c> can be constructed
	/// against a fake in tests.
	/// </summary>
	Task<AccountAuthService.DebugOttResponse?> GetDebugOttAsync();
}
