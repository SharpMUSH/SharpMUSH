using System.Diagnostics;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Times one function or command call by its own work: the wall time it ran, less the time it spent
/// evaluating its arguments and the time of every call nested inside it.
/// </summary>
/// <remarks>
/// A call's clock pauses its caller's for as long as it runs, so <c>null(iter(...))</c> charges the
/// iteration to ITER and the work under it, not to NULL. Argument evaluation pauses the clock of the
/// call it feeds, whether the arguments are evaluated before the call or by the function itself
/// (<c>iter</c>, <c>switch</c>, and every other no-parse function). The inclusive time is kept too,
/// for the queue diagnostics profile, which reports it as such.
/// </remarks>
public sealed class InvocationClock
{
	private static readonly AsyncLocal<InvocationClock?> Running = new();

	private readonly InvocationClock? _caller;
	private readonly long _started;
	private long _ownTicks;
	private long _resumedAt;
	private int _pauses;
	private long _stoppedAt;
	private readonly Lock _gate = new();

	private InvocationClock(InvocationClock? caller)
	{
		_caller = caller;
		_started = _resumedAt = Stopwatch.GetTimestamp();
	}

	/// <summary>Starts a call's clock, pausing the clock of the call it runs inside.</summary>
	public static InvocationClock Start()
	{
		var caller = Running.Value;
		caller?.Pause();
		var clock = new InvocationClock(caller);
		Running.Value = clock;
		return clock;
	}

	/// <summary>
	/// Pauses the clock of the call running here, if any, until the returned scope is disposed: for a
	/// deferred argument evaluated by the call it belongs to, which holds no reference to its clock.
	/// </summary>
	public static Paused PauseRunning() => Running.Value?.Pause() ?? default;

	/// <summary>Pauses this clock until the returned scope is disposed.</summary>
	public Paused Pause()
	{
		lock (_gate)
		{
			if (_stoppedAt == 0 && _pauses++ == 0)
				_ownTicks += Stopwatch.GetTimestamp() - _resumedAt;
		}

		return new Paused(this);
	}

	private void Resume()
	{
		lock (_gate)
		{
			if (_stoppedAt == 0 && --_pauses == 0)
				_resumedAt = Stopwatch.GetTimestamp();
		}
	}

	/// <summary>
	/// Stops the clock and resumes the caller's. Stopping again changes nothing, so a call that turns
	/// out not to be one (prose) can stop early and leave its caller to time what follows.
	/// </summary>
	public void Stop()
	{
		lock (_gate)
		{
			if (_stoppedAt != 0) return;
			_stoppedAt = Stopwatch.GetTimestamp();
			if (_pauses == 0) _ownTicks += _stoppedAt - _resumedAt;
		}

		Running.Value = _caller;
		_caller?.Resume();
	}

	/// <summary>The call's own time so far, in milliseconds.</summary>
	public double OwnMilliseconds
	{
		get
		{
			lock (_gate)
			{
				var running = _stoppedAt == 0 && _pauses == 0 ? Stopwatch.GetTimestamp() - _resumedAt : 0;
				return Stopwatch.GetElapsedTime(0, _ownTicks + running).TotalMilliseconds;
			}
		}
	}

	/// <summary>The call's wall time so far, nested calls and argument evaluation included, in milliseconds.</summary>
	public double InclusiveMilliseconds
	{
		get
		{
			lock (_gate)
				return Stopwatch.GetElapsedTime(_started, _stoppedAt == 0 ? Stopwatch.GetTimestamp() : _stoppedAt).TotalMilliseconds;
		}
	}

	/// <summary>A pause, ended by disposing it.</summary>
	public readonly struct Paused(InvocationClock? clock) : IDisposable
	{
		public void Dispose() => clock?.Resume();
	}
}
