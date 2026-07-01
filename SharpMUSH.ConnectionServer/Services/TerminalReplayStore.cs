using System.Collections.Concurrent;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Per-handle monotonic output sequence + a bounded, short-lived replay buffer. Used only when
/// sequenced output is enabled (WebTransport feature flag). Backs the fallback replay path: when
/// a client reconnects fresh (migration failed) it acks the highest seq it saw and the server
/// replays buffered frames after it. Bounded by count and age to stay "minimal" (not full survival).
/// </summary>
public sealed class TerminalReplayStore
{
	private const int MaxFramesPerHandle = 200;
	private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

	private readonly ConcurrentDictionary<long, HandleBuffer> _buffers = new();
	private readonly Func<DateTimeOffset> _now;

	public TerminalReplayStore() : this(() => DateTimeOffset.UtcNow) { }

	// Test seam: inject a clock so age-based eviction is deterministic.
	public TerminalReplayStore(Func<DateTimeOffset> now) => _now = now;

	/// <summary>
	/// Assigns the next sequence number for a handle (1-based), wraps the raw UTF-8 output in a
	/// <see cref="SeqEnvelope"/>, records the wrapped frame for replay, and returns both. Wrapping
	/// happens here so seq assignment, wrapping, and recording are atomic under the handle lock.
	/// </summary>
	public (long Seq, byte[] Wrapped) Append(long handle, byte[] rawUtf8)
	{
		var buffer = _buffers.GetOrAdd(handle, _ => new HandleBuffer());
		return buffer.Append(rawUtf8, _now(), MaxFramesPerHandle);
	}

	/// <summary>Returns recorded frames whose seq is greater than <paramref name="lastSeq"/>, oldest first.</summary>
	public IReadOnlyList<byte[]> After(long handle, long lastSeq)
		=> _buffers.TryGetValue(handle, out var buffer)
			? buffer.After(lastSeq, _now(), MaxAge)
			: [];

	public void Drop(long handle) => _buffers.TryRemove(handle, out _);

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
				var result = new List<byte[]>();
				foreach (var entry in _entries)
					if (entry.Seq > lastSeq && entry.At >= cutoff)
						result.Add(entry.Payload);
				return result;
			}
		}

		private sealed record Entry(long Seq, byte[] Payload, DateTimeOffset At);
	}
}
