using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="ILoginFailureTracker"/>
/// <remarks>
/// Like PennMUSH it keeps only the most recent <see cref="Capacity"/> entries from every address
/// together, so a flood of failures costs a fixed amount of memory.
/// </remarks>
public sealed class LoginFailureTracker(TimeProvider clock) : ILoginFailureTracker
{
	public const int Capacity = 1000;
	public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

	private readonly record struct Entry(string? Address, DateTimeOffset At, long? Attempt);

	private readonly Lock _gate = new();
	private readonly Entry[] _entries = new Entry[Capacity];
	private readonly Dictionary<long, int> _attempts = [];
	private int _next;
	private int _count;

	public bool TryBegin(string address, long handle, uint limit)
	{
		lock (_gate)
		{
			if (CountLocked(address) >= limit) return false;

			_attempts[handle] = _next;
			_entries[_next] = new Entry(address, clock.GetUtcNow(), handle);
			_next = (_next + 1) % Capacity;
			_count = Math.Min(_count + 1, Capacity);
			return true;
		}
	}

	public int Fail(long handle)
	{
		lock (_gate)
		{
			if (TakeAttemptLocked(handle) is not int slot) return 0;

			var entry = _entries[slot];
			_entries[slot] = entry with { Attempt = null };
			return CountLocked(entry.Address!);
		}
	}

	public void End(long handle)
	{
		lock (_gate)
		{
			if (TakeAttemptLocked(handle) is int slot)
			{
				_entries[slot] = _entries[slot] with { Address = null };
			}
		}
	}

	public int Count(string address)
	{
		lock (_gate)
		{
			return CountLocked(address);
		}
	}

	/// <summary>The slot still holding the attempt on <paramref name="handle"/>, if a newer entry has not replaced it.</summary>
	private int? TakeAttemptLocked(long handle)
		=> _attempts.Remove(handle, out var slot) && _entries[slot].Attempt == handle ? slot : null;

	private int CountLocked(string address)
	{
		var since = clock.GetUtcNow() - Window;
		var matches = 0;
		for (var i = 1; i <= _count; i++)
		{
			var entry = _entries[(_next - i + Capacity) % Capacity];
			if (entry.At < since) break;
			if (entry.Address == address) matches++;
		}

		return matches;
	}
}
