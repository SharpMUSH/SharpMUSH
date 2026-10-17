using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;

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
}
