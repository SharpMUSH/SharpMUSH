using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// One-Time Token store for web-based MUSH authentication.
/// <para>
/// Flow: API server validates MUSH credentials → calls <see cref="CreateTokenAsync"/> →
/// returns token to browser → browser opens WebSocket → sends <c>connect token &lt;ott&gt;</c> →
/// ConnectionServer calls <see cref="ValidateAndConsumeAsync"/> → binds player.
/// </para>
/// Tokens are short-lived (default 60 s) and single-use; once consumed they are deleted.
/// </summary>
public interface IOttStore
{
	/// <summary>
	/// Create a new one-time token bound to <paramref name="playerRef"/> with the given TTL.
	/// </summary>
	Task<string> CreateTokenAsync(DBRef playerRef, TimeSpan ttl, CancellationToken ct = default);

	/// <summary>
	/// Validate a token and — if valid and unexpired — atomically consume it and return the
	/// bound <see cref="DBRef"/>.  Returns <c>null</c> if the token is unknown or expired.
	/// </summary>
	Task<DBRef?> ValidateAndConsumeAsync(string token, CancellationToken ct = default);
}
