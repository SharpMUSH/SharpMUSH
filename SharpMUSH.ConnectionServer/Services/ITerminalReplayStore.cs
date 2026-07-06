namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Per-session terminal output history for reconnect replay. Each append assigns a monotonic
/// per-session sequence, wraps the frame in a <see cref="SeqEnvelope"/>, and records it; on a fresh
/// reconnect the server replays frames after the client's acked sequence.
/// Implementations: an in-memory bounded buffer, and a NATS JetStream-backed store that survives a
/// ConnectionServer restart / instance change.
///
/// The key is a per-incarnation <c>session</c> id, NOT the connection handle: handles are reused
/// (recycled by <c>DescriptorGeneratorService</c>), so keying replay on the handle would let a new
/// occupant replay a prior session's output — a cross-session leak. A fresh session mints a new id.
/// </summary>
public interface ITerminalReplayStore
{
	/// <summary>Assigns the next seq for the session, wraps + records the frame, returns both.</summary>
	ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(string session, byte[] rawUtf8, CancellationToken ct = default);

	/// <summary>Wrapped frames with seq greater than <paramref name="lastSeq"/>, oldest first.</summary>
	ValueTask<IReadOnlyList<byte[]>> AfterAsync(string session, long lastSeq, CancellationToken ct = default);

	/// <summary>Discards a session's buffered history.</summary>
	ValueTask DropAsync(string session, CancellationToken ct = default);
}
