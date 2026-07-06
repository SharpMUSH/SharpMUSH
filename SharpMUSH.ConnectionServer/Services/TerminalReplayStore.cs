using System.Collections.Concurrent;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// In-memory <see cref="ITerminalReplayStore"/>: a per-handle bounded, short-lived output buffer.
/// Bounded by count and age to stay "minimal". Does NOT survive a ConnectionServer restart — use
/// <see cref="JetStreamTerminalReplayStore"/> for durable, restart-survivable replay.
/// </summary>
public sealed class TerminalReplayStore : ITerminalReplayStore
{
	private const int MaxFramesPerHandle = 200;
	private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<string, HandleBuffer> _buffers = new();
	private readonly Func<DateTimeOffset> _now;

	public TerminalReplayStore() : this(() => DateTimeOffset.UtcNow) { }

	// Test seam: inject a clock so age-based eviction is deterministic.
	public TerminalReplayStore(Func<DateTimeOffset> now) => _now = now;

	public ValueTask<(long Seq, byte[] Wrapped)> AppendAsync(string session, byte[] rawUtf8, CancellationToken ct = default)
	{
		var buffer = _buffers.GetOrAdd(session, _ => new HandleBuffer());
		return ValueTask.FromResult(buffer.Append(rawUtf8, _now(), MaxFramesPerHandle));
	}

	public ValueTask<IReadOnlyList<byte[]>> AfterAsync(string session, long lastSeq, CancellationToken ct = default)
		=> ValueTask.FromResult(_buffers.TryGetValue(session, out var buffer)
			? buffer.After(lastSeq, _now(), MaxAge)
			: []);

	public ValueTask DropAsync(string session, CancellationToken ct = default)
	{
		_buffers.TryRemove(session, out _);
		return ValueTask.CompletedTask;
	}

	private sealed class HandleBuffer
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

		public IReadOnlyList<byte[]> After(long lastSeq, DateTimeOffset now, TimeSpan maxAge)
		{
			lock (_gate)
			{
				var cutoff = now - maxAge;
				return _entries
					.Where(entry => entry.Seq > lastSeq && entry.At >= cutoff)
					.Select(entry => entry.Payload)
					.ToList();
			}
		}

		private sealed record Entry(long Seq, byte[] Payload, DateTimeOffset At);
	}
}
