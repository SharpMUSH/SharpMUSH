namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>One monotonic elapsed-time deadline for an evaluation, including async I/O.
/// This is deliberately not process CPU time, which cannot be attributed to an async job.</summary>
public sealed class ExecutionBudget : IDisposable
{
	private static readonly AsyncLocal<ExecutionBudget?> Ambient = new();
	private readonly CancellationTokenSource _cancellation;
	private readonly CancellationTokenSource _deadline;
	// Read from the source once: a source's Token throws once it is disposed, and work that outlives
	// the budget still asks for it.
	private readonly CancellationToken _token;
	private readonly CancellationToken _cancelledBy;
	private readonly TimeProvider _clock;
	private readonly long _started;
	private readonly TimeSpan _duration;
	// The deadline in Environment.TickCount64 milliseconds.
	private readonly long _expiresAt;
	// 0 while running, 1 once ended within the deadline, 2 once ended past it.
	private int _ended;
	public const string Error = "#-1 EXECUTION TIME LIMIT EXCEEDED";
	public static ExecutionBudget? Current => Ambient.Value;
	public static CancellationToken CurrentToken => Current?.Token ?? CancellationToken.None;
	public ExecutionBudget(TimeSpan duration, CancellationToken cancellationToken = default)
		: this(duration, cancellationToken, TimeProvider.System) { }

	internal ExecutionBudget(TimeSpan duration, CancellationToken cancellationToken, TimeProvider timeProvider)
	{
		if (duration != Timeout.InfiniteTimeSpan && (duration < TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue - 1))
			throw new ArgumentOutOfRangeException(nameof(duration));
		_duration = duration;
		_clock = timeProvider;
		_started = timeProvider.GetTimestamp();
		_expiresAt = duration == Timeout.InfiniteTimeSpan ? long.MaxValue
			: Environment.TickCount64 + (long)Math.Ceiling(duration.TotalMilliseconds) + 1;
		_cancelledBy = cancellationToken;
		_deadline = new CancellationTokenSource(duration, timeProvider);
		if (duration == TimeSpan.Zero) _deadline.Cancel();
		_cancellation = cancellationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token)
			: _deadline;
		_token = _cancellation.Token;
	}
	public CancellationToken Token => _token;
	/// <summary>What cancels this budget other than its own deadline: a budget that replaces it for
	/// part of the work (a timer of its own, not the rest of this one) still stops with it.</summary>
	public CancellationToken CancelledBy => _cancelledBy;
	public static ExecutionBudget FromMilliseconds(uint milliseconds, CancellationToken token = default)
		=> new(milliseconds == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(milliseconds), token);
	public TimeSpan Remaining => _duration == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : TimeSpan.FromTicks(Math.Max(0, (_duration - _clock.GetElapsedTime(_started)).Ticks));
	// Timer callbacks and the clock can cross their boundary at different granularities. Every function call
	// asks this several times, so it reads the coarse tick count rather than the precise clock: a tick or so
	// of slack against the deadline timer, which still fires on time.
	public bool IsExpired => Volatile.Read(ref _ended) switch
	{
		0 => _deadline.IsCancellationRequested || Environment.TickCount64 >= _expiresAt,
		var ended => ended == 2
	};
	public bool IsCancelled => _cancellation.IsCancellationRequested;
	public bool IsExceeded => IsExpired || IsCancelled;
	public void ThrowIfExceeded()
	{
		if (IsExpired) throw new OperationCanceledException(Error, Token);
		Token.ThrowIfCancellationRequested();
	}
	/// <summary><see cref="ThrowIfExceeded"/>, answering what is left of the budget: one clock read for
	/// both, for a loop that checks every row.</summary>
	public TimeSpan RemainingOrThrow()
	{
		var remaining = Remaining;
		var expired = Volatile.Read(ref _ended) == 0 ? _deadline.IsCancellationRequested || remaining == TimeSpan.Zero : IsExpired;
		if (expired) throw new OperationCanceledException(Error, Token);
		Token.ThrowIfCancellationRequested();
		return remaining;
	}
	public IDisposable Enter()
	{
		var previous = Ambient.Value;
		Ambient.Value = this;
		return new Scope(previous);
	}
	/// <summary>Links a request lifetime to the current deadline without extending it.
	/// The scope must remain active until awaited work or stream enumeration settles.</summary>
	public static IDisposable EnterLinked(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Current?.ThrowIfExceeded();
		if (!cancellationToken.CanBeCanceled || cancellationToken == CurrentToken)
			return new Scope(Current);
		return new LinkedScope(cancellationToken);
	}

	private sealed class LinkedScope : IDisposable
	{
		private readonly CancellationTokenSource _cancellation;
		private readonly ExecutionBudget _budget;
		private readonly IDisposable _scope;

		public LinkedScope(CancellationToken token)
		{
			var remaining = Current?.Remaining ?? TimeSpan.MaxValue;
			_cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, CurrentToken);
			_budget = new ExecutionBudget(remaining == TimeSpan.MaxValue ? Timeout.InfiniteTimeSpan : remaining, _cancellation.Token);
			_scope = _budget.Enter();
		}

		public void Dispose()
		{
			_scope.Dispose();
			_budget.Dispose();
			_cancellation.Dispose();
		}
	}

	private sealed class Scope(ExecutionBudget? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
	/// <summary>Ends the budget. Work that outlives it, such as a continuation or a <c>Task.Run</c> that captured
	/// it as <see cref="Current"/>, sees it cancelled from here on, never disposed.</summary>
	public void Dispose()
	{
		if (Interlocked.CompareExchange(ref _ended, IsExpired ? 2 : 1, 0) != 0) return;
		_cancellation.Cancel();
		if (!ReferenceEquals(_cancellation, _deadline)) _cancellation.Dispose();
		_deadline.Dispose();
	}
}
