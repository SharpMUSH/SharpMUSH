using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class ExecutionBudgetCancellationTests
{
	private sealed class ManualTimerProvider : TimeProvider
	{
		private Action? _fire;
		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			_fire = () => callback(state);
			return new ManualTimer(() => _fire = null);
		}
		public void FireDeadline() => _fire!();
		private sealed class ManualTimer(Action dispose) : ITimer
		{
			public bool Change(TimeSpan dueTime, TimeSpan period) => true;
			public void Dispose() => dispose();
			public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task TimerCancellationIsExpiryEvenBeforeMonotonicClockBoundary(bool alsoCancelledByCaller)
	{
		var timer = new ManualTimerProvider();
		using var caller = new CancellationTokenSource();
		using var budget = new ExecutionBudget(TimeSpan.FromMinutes(1), caller.Token, timer);
		if (alsoCancelledByCaller) caller.Cancel();
		timer.FireDeadline();
		await Assert.That(budget.Remaining).IsGreaterThan(TimeSpan.Zero);
		await Assert.That(budget.Token.IsCancellationRequested).IsTrue();
		await Assert.That(budget.IsExpired).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CallerCancellationDoesNotBecomeDeadlineExpiry(bool unlimited)
	{
		using var caller = new CancellationTokenSource();
		using var budget = new ExecutionBudget(unlimited ? Timeout.InfiniteTimeSpan : TimeSpan.FromMinutes(1), caller.Token);
		caller.Cancel();
		await Assert.That(budget.IsCancelled).IsTrue();
		await Assert.That(budget.IsExpired).IsFalse();
	}
}
