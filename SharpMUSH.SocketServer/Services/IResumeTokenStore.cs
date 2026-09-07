namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Mints and resolves opaque resume tokens binding a fresh reconnect back to a prior connection within a
/// short grace window. A token carries both the <c>handle</c> (to rebind the live session) and its
/// per-incarnation <c>session</c> id (to replay that incarnation's buffered output — never a later
/// occupant's, even when the handle has since been recycled). Implementations: in-memory, and a NATS
/// KV-backed store that survives a ConnectionServer restart / instance change.
/// </summary>
public interface IResumeTokenStore
{
	ValueTask<string> MintAsync(long handle, string session, CancellationToken ct = default);

	/// <summary>Read-only inspection. Never use this to authorize a resume; use TryConsumeAsync.</summary>
	ValueTask<(bool Found, long Handle, string Session)> TryResolveAsync(string token, CancellationToken ct = default);

	/// <summary>Atomically spends an unexpired token; at most one caller receives its binding.</summary>
	ValueTask<(bool Found, long Handle, string Session)> TryConsumeAsync(string token, CancellationToken ct = default);

	/// <summary>Revokes every token belonging to a session after logout, boot, or grace expiry.</summary>
	ValueTask RevokeSessionAsync(string session, CancellationToken ct = default);

	ValueTask InvalidateAsync(string token, CancellationToken ct = default);
}
