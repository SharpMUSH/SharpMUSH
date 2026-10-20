using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="ILoginFailureTracker"/>
/// <remarks>
/// Like PennMUSH it keeps only the most recent <see cref="Capacity"/> failures from every address
/// together, so a flood of failures costs a fixed amount of memory.
/// </remarks>
public sealed class LoginFailureTracker(TimeProvider clock) : ILoginFailureTracker
{
	public const int Capacity = 1000;
	public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

	private readonly Lock _gate = new();
	private readonly (string Address, DateTimeOffset At)[] _failures = new (string, DateTimeOffset)[Capacity];
	private int _next;
	private int _count;

	public int Record(string address)
	{
		lock (_gate)
		{
			_failures[_next] = (address, clock.GetUtcNow());
			_next = (_next + 1) % Capacity;
			_count = Math.Min(_count + 1, Capacity);
			return CountLocked(address);
		}
	}

	public int Count(string address)
	{
		lock (_gate)
		{
			return CountLocked(address);
		}
	}

	private int CountLocked(string address)
	{
		var since = clock.GetUtcNow() - Window;
		var matches = 0;
		for (var i = 1; i <= _count; i++)
		{
			var (failedAddress, at) = _failures[(_next - i + Capacity) % Capacity];
			if (at < since) break;
			if (failedAddress == address) matches++;
		}

		return matches;
	}
}
