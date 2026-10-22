using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Mcp;
using SharpMUSH.Tests.Integration.AnalysisTransport;

namespace SharpMUSH.Tests.Integration.Mcp;

public class McpAnalysisExecutorTests
{
	private sealed class DefaultOptions : IOptionsWrapper<SharpMUSHOptions>
	{
		public SharpMUSHOptions CurrentValue { get; } = SharpMUSHOptions.Default();
	}

	internal static IMushCodeAnalyzer OfflineAnalyzer()
	{
		var parser = new MUSHCodeParser(NullLogger<MUSHCodeParser>.Instance,
			new FunctionLibraryService(), new CommandLibraryService(), new DefaultOptions(),
			new ServiceCollection().BuildServiceProvider());
		return new MushCodeAnalyzer(parser);
	}

	[Test]
	public async Task Dispose_CancelsActualSyntaxAndWaitsForWorkerTermination()
	{
		var analyzer = OfflineAnalyzer();
		var executor = new McpAnalysisExecutor(Options.Create(new McpOptions { AnalysisTimeout = TimeSpan.FromSeconds(10) }));
		var code = string.Concat(Enumerable.Repeat("think x\n", 5 * 1024 * 1024 / 8));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = executor.ExecuteAsync(() => { entered.SetResult(); return analyzer.Validate(code, MushAnalysisMode.CommandsPerLine); });
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			executor.Dispose();
			await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
			await Assert.That(async () => await executor.ExecuteAsync(() => 1)).Throws<ObjectDisposedException>();
		}
		finally
		{
			executor.Dispose();
			try { await pending; }
			catch (OperationCanceledException) { }
		}
	}

	[Test]
	public async Task CancelledBudget_PropagatesFromActualSyntaxInsteadOfBecomingDiagnostics()
	{
		var analyzer = OfflineAnalyzer();
		using var budget = new ExecutionBudget(TimeSpan.Zero);
		using var scope = budget.Enter();
		await Assert.That(() => analyzer.Validate("add(1,2)")).Throws<OperationCanceledException>();
	}

	[Test]
	public async Task Cancellation_StopsActualLargeLiteralLexing()
	{
		var analyzer = OfflineAnalyzer();
		var text = "lit(" + new string('x', 5 * 1024 * 1024 - 5) + ")";
		using var cancellation = new CancellationTokenSource();
		var running = Task.Run(() =>
		{
			using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
			using var scope = budget.Enter();
			cancellation.CancelAfter(TimeSpan.FromMilliseconds(1));
			return analyzer.Validate(text);
		});
		await Assert.That(async () => await running.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
	}

	[Test]
	public async Task Timeout_DoesNotReleaseSlotUntilActualWorkerTerminates()
	{
		var clock = new ManualClock();
		var options = new McpOptions { MaxConcurrentAnalyses = 1, MaxQueuedAnalyses = 0 };
		using var analysis = new AnalysisExecutor(Options.Create(new AnalysisExecutionOptions
		{
			MaxConcurrentAnalyses = options.MaxConcurrentAnalyses, MaxQueuedAnalyses = options.MaxQueuedAnalyses,
			AnalysisTimeout = options.AnalysisTimeout
		}), clock: clock);
		using var executor = new McpAnalysisExecutor(analysis);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var deadlineObserved = new TaskCompletionSource<ExecutionBudget>(TaskCreationOptions.RunContinuationsAsynchronously);
		var workerExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var finish = new ManualResetEventSlim();
		var pending = executor.ExecuteAsync(() =>
		{
			try
			{
				var budget = ExecutionBudget.Current ?? throw new InvalidOperationException("Analysis worker has no execution budget.");
				using var registration = budget.Token.Register(() => deadlineObserved.TrySetResult(budget));
				entered.SetResult();
				finish.Wait();
				return 1;
			}
			finally { workerExited.TrySetResult(); }
		});
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Assert.That(deadlineObserved.Task.IsCompleted).IsFalse();
			clock.Advance(options.AnalysisTimeout);
			var expiredBudget = await deadlineObserved.Task.WaitAsync(TimeSpan.FromSeconds(30));
			await Assert.That(expiredBudget.IsExpired).IsTrue();
			await Assert.That(pending.IsCompleted).IsFalse();
			await Assert.That(async () => await executor.ExecuteAsync(() => 2)).Throws<InvalidOperationException>();
		}
		finally
		{
			finish.Set();
			try
			{
				try { await pending; }
				catch (OperationCanceledException) { }
			}
			finally
			{
				// An abandonment regression can complete pending before the delegate exits.
				if (entered.Task.IsCompletedSuccessfully) await workerExited.Task;
			}
		}
		await Assert.That(async () => await pending).Throws<OperationCanceledException>();
		await Assert.That(await executor.ExecuteAsync(() => 3)).IsEqualTo(3);
	}

	[Test]
	public async Task QueuedCancellation_RemovesAdmissionWithoutStartingWork()
	{
		using var executor = new McpAnalysisExecutor(Options.Create(new McpOptions
		{
			MaxConcurrentAnalyses = 1, MaxQueuedAnalyses = 1
		}));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var finish = new ManualResetEventSlim();
		using var cancellation = new CancellationTokenSource();
		var first = executor.ExecuteAsync(() => { entered.SetResult(); finish.Wait(); return 1; });
		var pending = new List<Task> { first };
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
			var ran = false;
			var queued = executor.ExecuteAsync(() => { ran = true; return 2; }, cancellation.Token);
			pending.Add(queued);
			await cancellation.CancelAsync();
			await Assert.That(async () => await queued.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
			await Assert.That(ran).IsFalse();
			var next = executor.ExecuteAsync(() => 3);
			pending.Add(next);
			finish.Set();
			await Assert.That(await first).IsEqualTo(1);
			await Assert.That(await next).IsEqualTo(3);
		}
		finally
		{
			finish.Set();
			try { await Task.WhenAll(pending); }
			catch (OperationCanceledException) { }
		}
	}
}
