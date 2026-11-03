namespace SharpMUSH.Library.Services;

/// <summary>
/// Times one function or command call by its own work: the wall time it ran, less the time it spent
/// evaluating its arguments and the time of every call nested inside it.
/// </summary>
/// <remarks>
/// A call's clock pauses its caller's for as long as it runs, so <c>null(iter(...))</c> charges the
/// iteration to ITER and the work under it, not to NULL. Argument evaluation pauses the clock of the
/// call it feeds, whether the arguments are evaluated before the call or by the call itself through
/// their deferred evaluation (<c>iter</c>, <c>switch</c>, <c>@switch</c>'s patterns). A no-parse
/// function that hands its raw argument text to <c>FunctionParse</c> (<c>uldefault</c>,
/// <c>localize</c>) pauses the clock around that parse in its current scope. The inclusive time is kept too, for the queue
/// diagnostics profile, which reports it as such.
/// <para>
/// A call's clock travels on the parser its body runs with (<c>IMUSHCodeParser.Clock</c>), so a call
/// nested in it finds its caller there with no ambient write per call. Code that evaluates on a fresh
/// parser of its own (an attribute lock, a channel format, a listener's filter) has no such clock, and
/// finds the call running in this flow instead: the flow keeps one slot, written in place as calls start
/// and stop, so only the first call in a flow writes the execution context.
/// </para>
/// </remarks>
public sealed class InvocationClock
{
	private static readonly AsyncLocal<Flow?> CurrentFlow = new();
	private static readonly AsyncLocal<TimeProvider?> Time = new();

	private readonly TimeProvider _time;
	private readonly InvocationClock? _caller;
	private readonly Flow _flow;
	private readonly InvocationClock? _flowPrevious;
	private readonly long _started;
	private long _ownTicks;
	private long _resumedAt;
	private int _pauses;
	private long _stoppedAt;
	private readonly Lock _gate = new();

	private InvocationClock(InvocationClock? caller, Flow flow)
	{
		_time = Time.Value ?? TimeProvider.System;
		_caller = caller;
		_flow = flow;
		_flowPrevious = flow.Running;
		_started = _resumedAt = _time.GetTimestamp();
	}

	/// <summary>
	/// Times the calls started in this flow by <paramref name="time"/> until the returned scope is
	/// disposed, so a test can read what each clock was charged without timing real work.
	/// </summary>
	public static IDisposable UseTime(TimeProvider time)
	{
		var previous = Time.Value;
		Time.Value = time;
		return new TimeScope(previous);
	}

	/// <summary>
	/// Starts a call's clock, pausing the clock of the call it runs inside: <paramref name="caller"/>, the
	/// clock on the parser the call was made with, or when that has none (a fresh parser) or it has
	/// stopped, the call running in this flow.
	/// </summary>
	public static InvocationClock Start(InvocationClock? caller)
	{
		var flow = CurrentFlow.Value;
		if (flow is null)
		{
			flow = new Flow();
			CurrentFlow.Value = flow;
		}

		var running = Running(caller, flow);
		running?.Pause();
		var clock = new InvocationClock(running, flow);
		flow.Running = clock;
		return clock;
	}

	/// <summary>
	/// Pauses the clock of the call running here until the returned scope is disposed: <paramref name="clock"/>,
	/// or the call running in this flow when it is null or stopped. For a deferred argument evaluated by the call
	/// it belongs to, and for a parse a function runs on its own argument text.
	/// </summary>
	public static Paused PauseRunning(InvocationClock? clock)
		=> Running(clock, CurrentFlow.Value)?.Pause() ?? default;

	private static InvocationClock? Running(InvocationClock? clock, Flow? flow)
		=> clock is { IsStopped: false } ? clock : flow?.Running;

	private bool IsStopped => Volatile.Read(ref _stoppedAt) != 0;

	/// <summary>Pauses this clock until the returned scope is disposed.</summary>
	public Paused Pause()
	{
		lock (_gate)
		{
			if (_stoppedAt == 0 && _pauses++ == 0)
				_ownTicks += _time.GetTimestamp() - _resumedAt;
		}

		return new Paused(this);
	}

	private void Resume()
	{
		lock (_gate)
		{
			if (_stoppedAt == 0 && --_pauses == 0)
				_resumedAt = _time.GetTimestamp();
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
			_stoppedAt = _time.GetTimestamp();
			if (_pauses == 0) _ownTicks += _stoppedAt - _resumedAt;
		}

		// Only the innermost call puts the flow back; one stopping out of turn leaves it be.
		if (_flow.Running == this) _flow.Running = _flowPrevious;
		_caller?.Resume();
	}

	/// <summary>The call's own time so far, in milliseconds.</summary>
	public double OwnMilliseconds
	{
		get
		{
			lock (_gate)
			{
				var running = _stoppedAt == 0 && _pauses == 0 ? _time.GetTimestamp() - _resumedAt : 0;
				return _time.GetElapsedTime(0, _ownTicks + running).TotalMilliseconds;
			}
		}
	}

	/// <summary>The call's wall time so far, nested calls and argument evaluation included, in milliseconds.</summary>
	public double InclusiveMilliseconds
	{
		get
		{
			lock (_gate)
				return _time.GetElapsedTime(_started, _stoppedAt == 0 ? _time.GetTimestamp() : _stoppedAt).TotalMilliseconds;
		}
	}

	/// <summary>The call running in one flow, written in place so a call does not copy the execution context.</summary>
	private sealed class Flow
	{
		public InvocationClock? Running;
	}

	private sealed class TimeScope(TimeProvider? previous) : IDisposable
	{
		public void Dispose() => Time.Value = previous;
	}

	/// <summary>A pause, ended by disposing it.</summary>
	public readonly struct Paused(InvocationClock? clock) : IDisposable
	{
		public void Dispose() => clock?.Resume();
	}
}
