namespace SharpMUSH.Client.Services;

/// <summary>
/// Narrow read-only view over the account session, exposed so
/// <c>AccountAuthStateProvider</c> can depend on an interface instead of the full
/// <see cref="AccountAuthService"/> surface (and so tests can fake it without HTTP/JS).
/// </summary>
public interface IAccountAuthState
{
	bool IsLoggedIn { get; }
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
	/// Development-only: get a debug OTT for player #1 without credentials. Exposed on the
	/// narrow interface (rather than requiring the concrete <see cref="AccountAuthService"/>)
	/// so <c>SharpMUSH.Client.Authentication.DebugAuthStateProvider</c> can be constructed
	/// against a fake in tests.
	/// </summary>
	Task<AccountAuthService.DebugOttResponse?> GetDebugOttAsync();
}
