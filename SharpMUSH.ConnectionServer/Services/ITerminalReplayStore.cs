namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Per-handle terminal output history for reconnect replay. Each append assigns a monotonic
/// per-handle sequence, wraps the frame in a <see cref="SeqEnvelope"/>, and records it; on a fresh
/// reconnect the server replays frames after the client's acked sequence.
/// Implementations: an in-memory bounded buffer, and a NATS JetStream-backed store that survives a
/// ConnectionServer restart / instance change.
/// </summary>
public interface ITerminalReplayStore
{
	/// <summary>Assigns the next seq for the handle, wraps + records the frame, returns both.</summary>
	ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(long handle, byte[] rawUtf8, CancellationToken ct = default);

	/// <summary>Wrapped frames with seq greater than <paramref name="lastSeq"/>, oldest first.</summary>
	ValueTask<IReadOnlyList<byte[]>> AfterAsync(long handle, long lastSeq, CancellationToken ct = default);

	/// <summary>Discards a handle's buffered history (after a successful resume).</summary>
	ValueTask DropAsync(long handle, CancellationToken ct = default);
}
