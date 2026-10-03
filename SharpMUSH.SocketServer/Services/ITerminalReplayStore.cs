namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// The frames a reconnect replays, read a page at a time so the memory a replay holds does not grow
/// with the session's retained output. Dispose it once the replay is sent or abandoned.
/// </summary>
public abstract class ReplayFrames : IAsyncDisposable
{
	/// <summary>
	/// The wrapped frames after the client's last sequence, oldest first. The count is fixed when the
	/// replay opens, so output appended meanwhile is not replayed and a busy session cannot make the
	/// replay endless. Throws <see cref="ReplayInterruptedException"/> if retained frames disappear
	/// part-way — the caller has then sent an incomplete history and must not complete the resume.
	/// </summary>
	public abstract IAsyncEnumerable<byte[]> ReadAsync(CancellationToken ct = default);

	public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Why a replay cannot be complete.</summary>
public enum ReplayGap
{
	/// <summary>Frames the client has not seen were evicted (by age, the byte budget, or a purge).</summary>
	Expired,

	/// <summary>More frames are retained than one replay may send (<c>Replay:MaxFrames</c>).</summary>
	OverBudget,
}

/// <summary>The requested history cannot be replayed in full; the client starts a fresh session.</summary>
public readonly record struct IncompleteReplay(ReplayGap Reason);

/// <summary>The outcome of opening a replay: the frames to send, or why there is no complete history.</summary>
public union ReplayOpening(ReplayFrames, IncompleteReplay);

/// <summary>Retained frames vanished while a replay was being sent.</summary>
public sealed class ReplayInterruptedException : Exception
{
	public ReplayInterruptedException() : base("The replay history stopped being complete while it was sent.") { }
}

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

	/// <summary>
	/// Opens the replay of frames after <paramref name="lastSeq"/>: the frames, when the whole history
	/// after it is retained and within budget, or the reason it is not.
	/// </summary>
	ValueTask<ReplayOpening> OpenAsync(string session, long lastSeq, CancellationToken ct = default);

	/// <summary>Releases a session's replay resources once it has ended for good.</summary>
	ValueTask DropAsync(string session, CancellationToken ct = default);
}
