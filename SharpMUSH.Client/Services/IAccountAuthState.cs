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

	/// <summary>Raised whenever login/logout changes the session.</summary>
	event Action? AuthStateChanged;
}
