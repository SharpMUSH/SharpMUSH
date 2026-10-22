using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

public class SharedExecutionTests
{
	[Test]
	public async Task AsyncWork_CancellationRetainsCapacityUntilActualTermination()
	{
		using var executor = new AnalysisExecutor(Options.Create(new AnalysisExecutionOptions { MaxConcurrentAnalyses = 1, MaxQueuedAnalyses = 0 }));
		using var cancellation = new CancellationTokenSource();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = executor.ExecuteAsync(async () => { entered.SetResult(); await finish.Task; return 1; }, cancellation.Token);
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			await cancellation.CancelAsync();
			// Allow cancellation continuations to run while the worker remains blocked.
			await Task.WhenAny(pending, Task.Delay(60));
			await Assert.That(pending.IsCompleted).IsFalse();
			await Assert.That(async () => await executor.ExecuteAsync(async () => { await Task.Yield(); return 2; })).Throws<InvalidOperationException>();
		}
		finally
		{
			finish.TrySetResult();
			try { await pending; }
			catch (OperationCanceledException) { }
		}
		await Assert.That(async () => await pending).Throws<OperationCanceledException>();
		await Assert.That(await executor.ExecuteAsync(() => Task.FromResult(3))).IsEqualTo(3);
	}

	[Test]
	public async Task AsyncWork_ElapsedDeadlineRetainsCapacityUntilActualTermination()
	{
		var clock = new ManualClock();
		using var executor = new AnalysisExecutor(Options.Create(new AnalysisExecutionOptions
		{
			MaxConcurrentAnalyses = 1, MaxQueuedAnalyses = 0, AnalysisTimeout = TimeSpan.FromMilliseconds(30)
		}), clock: clock);
		var entered = new TaskCompletionSource<ExecutionBudget>(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = executor.ExecuteAsync(async () => { entered.SetResult(ExecutionBudget.Current!); await finish.Task; return 1; });
		try
		{
			var budget = await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Assert.That(budget.IsExpired).IsFalse();
			clock.Advance(TimeSpan.FromMilliseconds(30));
			await Assert.That(budget.IsExpired).IsTrue();
			await Assert.That(pending.IsCompleted).IsFalse();
			await Assert.That(async () => await executor.ExecuteAsync(async () => { await Task.Yield(); return 2; })).Throws<InvalidOperationException>();
		}
		finally
		{
			finish.TrySetResult();
			try { await pending; }
			catch (OperationCanceledException) { }
		}
		await Assert.That(async () => await pending).Throws<OperationCanceledException>();
		await Assert.That(await executor.ExecuteAsync(() => Task.FromResult(3))).IsEqualTo(3);
	}

	[Test]
	public async Task QueuedWork_ElapsedDeadlineCountsTheAdmissionWait()
	{
		var clock = new ManualClock();
		using var executor = new AnalysisExecutor(Options.Create(new AnalysisExecutionOptions
		{
			MaxConcurrentAnalyses = 1, MaxQueuedAnalyses = 1, AnalysisTimeout = TimeSpan.FromMilliseconds(30)
		}), clock: clock);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var running = executor.ExecuteAsync(async () => { entered.SetResult(); await finish.Task; return 1; });
		var pending = new List<Task> { running };
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
			var ran = false;
			var queued = executor.ExecuteAsync(() => { ran = true; return Task.FromResult(2); });
			pending.Add(queued);
			clock.Advance(TimeSpan.FromMilliseconds(30));
			await Assert.That(async () => await queued.WaitAsync(TimeSpan.FromSeconds(30))).Throws<OperationCanceledException>();
			await Assert.That(ran).IsFalse();
			await Assert.That(running.IsCompleted).IsFalse();
		}
		finally
		{
			finish.TrySetResult();
			foreach (var task in pending)
			{
				try { await task; }
				catch (OperationCanceledException) { }
			}
		}
		await Assert.That(await executor.ExecuteAsync(() => Task.FromResult(3))).IsEqualTo(3);
	}
}
