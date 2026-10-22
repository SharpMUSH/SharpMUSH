namespace SharpMUSH.Tests.Integration.AnalysisTransport;

/// <summary>A clock that moves only when a test advances it. Its timers fire on <see cref="Advance"/>, against the
/// same time its timestamps read, so a deadline expires exactly when the test says and never on a busy runner.</summary>
internal sealed class ManualClock : TimeProvider
{
	private readonly Lock _gate = new();
	private readonly List<Deadline> _timers = [];
	private long _now;

	public override long TimestampFrequency => TimeSpan.TicksPerSecond;
	public override long GetTimestamp() => Volatile.Read(ref _now);

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		var timer = new Deadline(this, () => callback(state));
		timer.Change(dueTime, period);
		return timer;
	}

	public void Advance(TimeSpan by)
	{
		Deadline[] due;
		lock (_gate)
		{
			_now += by.Ticks;
			due = [.. _timers.Where(timer => timer.Due <= _now)];
			foreach (var timer in due) _timers.Remove(timer);
		}
		foreach (var timer in due) timer.Fire();
	}

	private sealed class Deadline(ManualClock clock, Action fire) : ITimer
	{
		public long Due { get; private set; }
		public void Fire() => fire();

		public bool Change(TimeSpan dueTime, TimeSpan period)
		{
			lock (clock._gate)
			{
				clock._timers.Remove(this);
				if (dueTime == Timeout.InfiniteTimeSpan) return true;
				Due = clock._now + dueTime.Ticks;
				clock._timers.Add(this);
			}
			return true;
		}

		public void Dispose()
		{
			lock (clock._gate) clock._timers.Remove(this);
		}

		public ValueTask DisposeAsync()
		{
			Dispose();
			return ValueTask.CompletedTask;
		}
	}
}
