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
		await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
		executor.Dispose();
		await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
		await Assert.That(async () => await executor.ExecuteAsync(() => 1)).Throws<ObjectDisposedException>();
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
		using var executor = new McpAnalysisExecutor(Options.Create(new McpOptions
		{
			MaxConcurrentAnalyses = 1, MaxQueuedAnalyses = 0, AnalysisTimeout = TimeSpan.FromMilliseconds(100)
		}));
		using var entered = new ManualResetEventSlim();
		using var finish = new ManualResetEventSlim();
		var pending = executor.ExecuteAsync(() =>
		{
			entered.Set();
			if (!finish.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
			return 1;
		});
		if (!entered.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
		var finishedBeforeWorker = pending.Wait(TimeSpan.FromMilliseconds(150));
		try
		{
			await Assert.That(finishedBeforeWorker).IsFalse();
			await Assert.That(async () => await executor.ExecuteAsync(() => 2)).Throws<InvalidOperationException>();
		}
		finally { finish.Set(); }
		await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
		await Assert.That(await executor.ExecuteAsync(() => 3)).IsEqualTo(3);
	}

	[Test]
	public async Task QueuedCancellation_RemovesAdmissionWithoutStartingWork()
	{
		using var executor = new McpAnalysisExecutor(Options.Create(new McpOptions
		{
			MaxConcurrentAnalyses = 1, MaxQueuedAnalyses = 1
		}));
		using var entered = new ManualResetEventSlim();
		using var finish = new ManualResetEventSlim();
		var first = executor.ExecuteAsync(() => { entered.Set(); if (!finish.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException(); return 1; });
		if (!entered.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
		using var cancellation = new CancellationTokenSource();
		var ran = false;
		var queued = executor.ExecuteAsync(() => { ran = true; return 2; }, cancellation.Token);
		cancellation.Cancel();
		try
		{
			await Assert.That(async () => await queued.WaitAsync(TimeSpan.FromSeconds(3))).Throws<OperationCanceledException>();
			await Assert.That(ran).IsFalse();
			var next = executor.ExecuteAsync(() => 3);
			finish.Set();
			await Assert.That(await first).IsEqualTo(1);
			await Assert.That(await next).IsEqualTo(3);
		}
		finally { finish.Set(); }
	}
}
