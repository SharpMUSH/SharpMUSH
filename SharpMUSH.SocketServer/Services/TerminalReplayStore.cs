using System.Collections.Concurrent;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// In-memory <see cref="ITerminalReplayStore"/>: a per-session bounded, short-lived output buffer.
/// Bounded by count and age to stay "minimal". Does NOT survive a ConnectionServer restart — use
/// <see cref="JetStreamTerminalReplayStore"/> for durable, restart-survivable replay.
/// </summary>
public sealed class TerminalReplayStore : ITerminalReplayStore
{
	private const int MaxFramesPerSession = 200;
	private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<string, SessionBuffer> _buffers = new();
	private readonly Func<DateTimeOffset> _now;

	public TerminalReplayStore() : this(() => DateTimeOffset.UtcNow) { }

	// Test seam: inject a clock so age-based eviction is deterministic.
	public TerminalReplayStore(Func<DateTimeOffset> now) => _now = now;

	public ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(string session, byte[] rawUtf8, CancellationToken ct = default)
	{
		var buffer = _buffers.GetOrAdd(session, _ => new SessionBuffer());
		return ValueTask.FromResult(buffer.Append(rawUtf8, _now(), MaxFramesPerSession));
	}

	public ValueTask<ReplayOpening> OpenAsync(string session, long lastSeq, CancellationToken ct = default)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(lastSeq);
		ct.ThrowIfCancellationRequested();
		if (!_buffers.TryGetValue(session, out var buffer))
			return ValueTask.FromResult<ReplayOpening>(lastSeq == 0
				? new SnapshotFrames([])
				: new IncompleteReplay(ReplayGap.Expired));
		return ValueTask.FromResult(buffer.Read(lastSeq, _now(), MaxAge));
	}

	public ValueTask DropAsync(string session, CancellationToken ct = default)
	{
		_buffers.TryRemove(session, out _);
		return ValueTask.CompletedTask;
	}

	private sealed class SessionBuffer
	{
		private readonly object _gate = new();
		private readonly LinkedList<Entry> _entries = new();
		private long _seq;

		public (long Seq, byte[] Wrapped) Append(byte[] rawUtf8, DateTimeOffset now, int maxFrames)
		{
			lock (_gate)
			{
				var seq = ++_seq;
				var wrapped = SeqEnvelope.Wrap(seq, rawUtf8);
				_entries.AddLast(new Entry(seq, wrapped, now));
				while (_entries.Count > maxFrames)
					_entries.RemoveFirst();
				return (seq, wrapped);
			}
		}

		public ReplayOpening Read(long lastSeq, DateTimeOffset now, TimeSpan maxAge)
		{
			lock (_gate)
			{
				var cutoff = now - maxAge;
				while (_entries.First is { } first && first.Value.At < cutoff)
					_entries.RemoveFirst();
				var earliest = _entries.First?.Value.Seq;
				var complete = earliest is { } seq ? lastSeq >= seq - 1 : lastSeq >= _seq;
				if (!complete) return new IncompleteReplay(ReplayGap.Expired);
				// At most MaxFramesPerSession frames, so a copy is already a bounded read.
				return new SnapshotFrames(_entries
					.Where(entry => entry.Seq > lastSeq)
					.Select(entry => entry.Payload)
					.ToList());
			}
		}

		private sealed record Entry(long Seq, byte[] Payload, DateTimeOffset At);
	}

	private sealed class SnapshotFrames(IReadOnlyList<byte[]> frames) : ReplayFrames
	{
		public override IAsyncEnumerable<byte[]> ReadAsync(CancellationToken ct = default) => frames.ToAsyncEnumerable();
	}
}
