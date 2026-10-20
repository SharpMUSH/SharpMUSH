using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="ILoginFailureTracker"/>
/// <remarks>
/// Like PennMUSH it keeps only the most recent <see cref="Capacity"/> failures from every address
/// together, so a flood of failures costs a fixed amount of memory. Attempts in progress are held
/// apart, so attempts that never fail cannot push failures out.
/// </remarks>
public sealed class LoginFailureTracker(TimeProvider clock) : ILoginFailureTracker
{
	public const int Capacity = 1000;
	public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

	private readonly record struct Entry(string Address, DateTimeOffset At);

	private readonly Lock _gate = new();
	private readonly Entry[] _entries = new Entry[Capacity];
	private readonly Dictionary<long, string> _attempts = [];
	private readonly Dictionary<string, int> _pending = [];
	private int _next;
	private int _count;

	public bool TryBegin(string address, long handle, uint limit)
	{
		lock (_gate)
		{
			if (_attempts.ContainsKey(handle)) return false;
			if (CountLocked(address) + _pending.GetValueOrDefault(address) >= limit) return false;

			_attempts[handle] = address;
			_pending[address] = _pending.GetValueOrDefault(address) + 1;
			return true;
		}
	}

	public int Fail(long handle)
	{
		lock (_gate)
		{
			if (TakeAttemptLocked(handle) is not { } address) return 0;

			_entries[_next] = new Entry(address, clock.GetUtcNow());
			_next = (_next + 1) % Capacity;
			_count = Math.Min(_count + 1, Capacity);
			return CountLocked(address);
		}
	}

	public void End(long handle)
	{
		lock (_gate)
		{
			TakeAttemptLocked(handle);
		}
	}

	public int Count(string address)
	{
		lock (_gate)
		{
			return CountLocked(address);
		}
	}

	/// <summary>Ends the attempt on <paramref name="handle"/>, returning its address if it was in progress.</summary>
	private string? TakeAttemptLocked(long handle)
	{
		if (!_attempts.Remove(handle, out var address)) return null;

		if (_pending[address] == 1)
		{
			_pending.Remove(address);
		}
		else
		{
			_pending[address]--;
		}

		return address;
	}

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
