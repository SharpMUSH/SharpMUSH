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

	/// <summary>Resolves a token to its handle and session id if present and unexpired.</summary>
	ValueTask<(bool Found, long Handle, string Session)> TryResolveAsync(string token, CancellationToken ct = default);

	ValueTask InvalidateAsync(string token, CancellationToken ct = default);
}
