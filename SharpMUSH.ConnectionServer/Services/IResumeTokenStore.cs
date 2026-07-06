namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Mints and resolves opaque resume tokens binding a fresh reconnect back to a prior handle within a
/// short grace window. Implementations: in-memory, and a NATS KV-backed store that survives a
/// ConnectionServer restart / instance change (so replay works after the server bounces).
/// </summary>
public interface IResumeTokenStore
{
	ValueTask<string> MintAsync(long handle, CancellationToken ct = default);

	/// <summary>Resolves a token to its handle if present and unexpired.</summary>
	ValueTask<(bool Found, long Handle)> TryResolveAsync(string token, CancellationToken ct = default);

	ValueTask InvalidateAsync(string token, CancellationToken ct = default);
}
