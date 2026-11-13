using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IMemoStore"/>
public sealed class MemoStore(IOptionsWrapper<SharpMUSHOptions> options, TimeProvider time) : IMemoStore
{
	private sealed record Slot(string Key, MemoEntry Entry, long Size, DateTimeOffset? Expires);

	private readonly Lock _gate = new();
	private readonly Dictionary<string, LinkedListNode<Slot>> _slots = new(StringComparer.Ordinal);
	private readonly LinkedList<Slot> _recency = [];
	private long _bytes;

	/// <inheritdoc />
	public Found<MemoEntry> Find(string key)
	{
		lock (_gate)
		{
			if (!_slots.TryGetValue(key, out var node)) return new NotFound();
			if (node.Value.Expires is { } expires && time.GetUtcNow() >= expires)
			{
				Remove(node);
				return new NotFound();
			}

			_recency.Remove(node);
			_recency.AddFirst(node);
			return node.Value.Entry;
		}
	}

	/// <inheritdoc />
	public void Keep(string key, MemoEntry entry)
	{
		var limits = options.CurrentValue.Limit;
		var budget = (long)limits.MemoMaxBytes;
		var size = SizeOf(key, entry);
		if (size > budget) return;

		var expires = limits.MemoWindow == 0 ? (DateTimeOffset?)null : time.GetUtcNow().AddSeconds(limits.MemoWindow);
		lock (_gate)
		{
			if (_slots.TryGetValue(key, out var existing)) Remove(existing);
			while (_bytes + size > budget && _recency.Last is { } oldest) Remove(oldest);

			_slots[key] = _recency.AddFirst(new Slot(key, entry, size, expires));
			_bytes += size;
		}
	}

	/// <inheritdoc />
	public void Forget(string key)
	{
		lock (_gate)
		{
			if (_slots.TryGetValue(key, out var node)) Remove(node);
		}
	}

	/// <inheritdoc />
	public (int Count, long Bytes) Usage
	{
		get
		{
			lock (_gate) return (_slots.Count, _bytes);
		}
	}

	private void Remove(LinkedListNode<Slot> node)
	{
		_recency.Remove(node);
		_slots.Remove(node.Value.Key);
		_bytes -= node.Value.Size;
	}

	/// <summary>An estimate of what an entry holds: its key, its answer (markup runs at a flat rate) and its reads.</summary>
	private static long SizeOf(string key, MemoEntry entry)
		=> 128 + 2L * key.Length + 2L * entry.Value.Text.Length + 64L * (entry.Value.Runs.IsDefaultOrEmpty ? 0 : entry.Value.Runs.Length)
			+ entry.Reads.Sum(read => read.Size);
}
