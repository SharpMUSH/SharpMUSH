namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Multi-use session token store for web account authentication.
/// <para>
/// After a successful account login via the API, an <see cref="IAccountSessionStore"/> token
/// is issued and returned to the browser. Subsequent API calls (character list, create character,
/// request OTT) authenticate by presenting this token. Tokens expire after 15 minutes of
/// inactivity and are renewed on each successful use.
/// </para>
/// </summary>
public interface IAccountSessionStore
{
	/// <summary>
	/// Create a new session token bound to <paramref name="accountId"/> with the given TTL.
	/// </summary>
	Task<string> CreateTokenAsync(string accountId, TimeSpan ttl, CancellationToken ct = default);

	/// <summary>
	/// Validates a token. If valid and unexpired, returns the bound account ID and slides
	/// the expiry window by the original TTL. Returns <c>null</c> if unknown or expired.
	/// </summary>
	Task<string?> ValidateAsync(string token, CancellationToken ct = default);

	/// <summary>Explicitly invalidates a token (logout).</summary>
	Task RevokeAsync(string token, CancellationToken ct = default);

	/// <summary>Invalidates every session token bound to the account (disable/ban).</summary>
	Task RevokeAllForAccountAsync(string accountId, CancellationToken ct = default);
}
