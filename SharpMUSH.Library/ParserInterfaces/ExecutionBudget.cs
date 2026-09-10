using System.Diagnostics;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>One monotonic elapsed-time deadline for an evaluation, including async I/O.
/// This is deliberately not process CPU time, which cannot be attributed to an async job.</summary>
public sealed class ExecutionBudget : IDisposable
{
	private static readonly AsyncLocal<ExecutionBudget?> Ambient = new();
	private readonly CancellationTokenSource _cancellation;
	private readonly CancellationTokenSource _deadline;
	private readonly long _started = Stopwatch.GetTimestamp();
	private readonly TimeSpan _duration;
	public const string Error = "#-1 EXECUTION TIME LIMIT EXCEEDED";
	public static ExecutionBudget? Current => Ambient.Value;
	public static CancellationToken CurrentToken => Current?.Token ?? CancellationToken.None;
	public ExecutionBudget(TimeSpan duration, CancellationToken cancellationToken = default)
		: this(duration, cancellationToken, TimeProvider.System) { }

	internal ExecutionBudget(TimeSpan duration, CancellationToken cancellationToken, TimeProvider timerProvider)
	{
		if (duration != Timeout.InfiniteTimeSpan && (duration < TimeSpan.Zero || duration.TotalMilliseconds > uint.MaxValue - 1))
			throw new ArgumentOutOfRangeException(nameof(duration));
		_duration = duration;
		_deadline = new CancellationTokenSource(duration, timerProvider);
		_cancellation = cancellationToken.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _deadline.Token)
			: _deadline;
	}
	public CancellationToken Token => _cancellation.Token;
	public static ExecutionBudget FromMilliseconds(uint milliseconds, CancellationToken token = default)
		=> new(milliseconds == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(milliseconds), token);
	public TimeSpan Remaining => _duration == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : TimeSpan.FromTicks(Math.Max(0, (_duration - Stopwatch.GetElapsedTime(_started)).Ticks));
	// Timer callbacks and Stopwatch can cross their boundary at different granularities.
	public bool IsExpired => _deadline.IsCancellationRequested || Remaining == TimeSpan.Zero;
	public bool IsCancelled => _cancellation.IsCancellationRequested;
	public bool IsExceeded => IsExpired || IsCancelled;
	public void ThrowIfExceeded()
	{
		if (IsExpired) throw new OperationCanceledException(Error, Token);
		Token.ThrowIfCancellationRequested();
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
	public void Dispose()
	{
		if (!ReferenceEquals(_cancellation, _deadline)) _cancellation.Dispose();
		_deadline.Dispose();
	}
}
