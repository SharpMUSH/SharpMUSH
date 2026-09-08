using System.Diagnostics;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>One monotonic elapsed-time deadline for an evaluation, including async I/O.
/// This is deliberately not process CPU time, which cannot be attributed to an async job.</summary>
public sealed class ExecutionBudget : IDisposable
{
	private static readonly AsyncLocal<ExecutionBudget?> Ambient = new();
	private readonly CancellationTokenSource _cancellation;
	private readonly long _started = Stopwatch.GetTimestamp();
	private readonly TimeSpan _duration;
	public const string Error = "#-1 EXECUTION TIME LIMIT EXCEEDED";
	public static ExecutionBudget? Current => Ambient.Value;
	public static CancellationToken CurrentToken => Current?.Token ?? CancellationToken.None;
	public ExecutionBudget(TimeSpan duration, CancellationToken cancellationToken = default)
	{
		_duration = duration;
		_cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		_cancellation.CancelAfter(duration);
	}
	public CancellationToken Token => _cancellation.Token;
	public TimeSpan Remaining => TimeSpan.FromTicks(Math.Max(0, (_duration - Stopwatch.GetElapsedTime(_started)).Ticks));
	public bool IsExceeded => Remaining == TimeSpan.Zero || _cancellation.IsCancellationRequested;
	public void ThrowIfExceeded()
	{
		if (IsExceeded) throw new OperationCanceledException(Error, Token);
	}
	public IDisposable Enter()
	{
		var previous = Ambient.Value;
		Ambient.Value = this;
		return new Scope(previous);
	}
	private sealed class Scope(ExecutionBudget? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
	public void Dispose() => _cancellation.Dispose();
}
