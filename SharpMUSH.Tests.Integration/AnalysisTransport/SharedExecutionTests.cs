using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

public class SharedExecutionTests
{
	[Test]
	public async Task AsyncWork_TimeoutRetainsCapacityUntilActualTermination()
	{
		using var executor = new AnalysisExecutor(Options.Create(new AnalysisExecutionOptions { MaxConcurrentAnalyses = 1, MaxQueuedAnalyses = 0, AnalysisTimeout = TimeSpan.FromMilliseconds(30) }));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = executor.ExecuteAsync(async () => { entered.SetResult(); await finish.Task; return 1; });
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
		await Task.Delay(60);
		try
		{
			await Assert.That(pending.IsCompleted).IsFalse();
			await Assert.That(async () => await executor.ExecuteAsync(async () => { await Task.Yield(); return 2; })).Throws<InvalidOperationException>();
		}
		finally { finish.SetResult(); }
		await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
		await Assert.That(await executor.ExecuteAsync(() => Task.FromResult(3))).IsEqualTo(3);
	}
}
