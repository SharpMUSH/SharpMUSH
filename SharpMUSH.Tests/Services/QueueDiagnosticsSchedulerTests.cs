using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Diagnostics;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class QueueDiagnosticsSchedulerTests
{
	private static Scheduler Create(QueueDiagnosticsRecorder? recorder, uint capacity = 10, uint milliseconds = 1000,
		IMUSHCodeParser? parser = null, IScheduler? scheduled = null)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { GlobalQueueLimit = capacity, PlayerQueueLimit = capacity, QueueEntryCpuTime = milliseconds } });
		var factory = Substitute.For<ISchedulerFactory>();
		if (scheduled is not null) factory.GetScheduler().Returns(scheduled);
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(), factory,
			Substitute.For<IAttributeService>(), QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance,
			options, diagnostics: recorder);
	}
	private static async Task HistoryCount(QueueDiagnosticsRecorder recorder, int count)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (recorder.Recent().Count < count) await Task.Delay(5, timeout.Token);
	}

	[Test]
	[NotInParallel]
	public async Task RepresentativeQueueOverheadPreservesEveryInvocationAndSideEffect()
	{
		const int count = 5000;
		// Discard round zero when reporting measurements: it includes process/JIT warm-up.
		for (var round = 0; round < 4; round++)
			foreach (var mode in new[] { "disabled", "history", "profile" })
			{
				var recorder = mode == "disabled" ? null : new QueueDiagnosticsRecorder();
				if (mode == "profile") recorder!.StartProfile(new("benchmark", new DBRef(1, 1), new DBRef(1, 1)), TimeSpan.FromSeconds(60));
				using var telemetry = new TelemetryService(recorder is null ? [] : [recorder]);
				await using var queue = Create(recorder, capacity: count + 1);
				// Warm the queue and each observer mode before comparing their steady-state hot paths.
				for (var warmup = 0; warmup < 300; warmup++)
					await queue.EnqueueWork(() =>
					{
						telemetry.RecordFunctionInvocation("add", .01, true);
						telemetry.RecordFunctionInvocation("mul", .01, true);
						telemetry.RecordCommandInvocation("think", .03, true);
						return ValueTask.FromResult<CallState?>(null);
					}, "warmup", "enqueue");
				using var warmupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
				while (queue.GetQueueUsage().Total != 0) await Task.Delay(1, warmupTimeout.Token);
				recorder?.DrainProfileSamples();
				var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				var observed = 0;
				var allocatedBefore = GC.GetTotalAllocatedBytes();
				var elapsed = System.Diagnostics.Stopwatch.StartNew();
				for (var i = 0; i < count; i++)
				{
					var expected = i;
					var admitted = await queue.EnqueueWork(() =>
					{
						if (observed != expected) throw new InvalidOperationException("Queue order changed");
						telemetry.RecordFunctionInvocation("add", .01, true);
						telemetry.RecordFunctionInvocation("mul", .01, true);
						telemetry.RecordCommandInvocation("think", .03, true);
						if (++observed == count) completed.SetResult();
						return ValueTask.FromResult<CallState?>(null);
					}, "benchmark", "enqueue");
					await Assert.That(admitted.Accepted).IsTrue();
				}
				await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
				using var drainedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
				while (queue.GetQueueUsage().Total != 0) await Task.Delay(1, drainedTimeout.Token);
				elapsed.Stop();
				Console.WriteLine($"Queue diagnostics benchmark: round={round}, mode={mode}, entries={count}, elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F2}, allocated_bytes={GC.GetTotalAllocatedBytes() - allocatedBefore}");
				await Assert.That(observed).IsEqualTo(count);
				if (recorder is not null)
					await Assert.That(recorder.Recent().All(row => row.InvocationCount == 3 && row.Outcome == QueueOutcome.Completed)).IsTrue();
			}
	}

	[Test]
	public async Task CompletedQueueEntryPreservesOrderAndCountsExistingTelemetryHooks()
	{
		var recorder = new QueueDiagnosticsRecorder();
		await using var queue = Create(recorder);
		var seen = new List<int>();
		for (var i = 0; i < 3; i++)
		{
			var value = i;
			await queue.EnqueueWork(() =>
			{
				seen.Add(value);
				recorder.RecordInvocation(new(TelemetryInvocationKind.Function, "add", 1, true));
				return ValueTask.FromResult<CallState?>(null);
			}, "not-stored-secret", "enqueue");
		}
		await HistoryCount(recorder, 3);
		await Assert.That(seen.SequenceEqual([0, 1, 2])).IsTrue();
		await Assert.That(recorder.Recent().All(row => row.Outcome == QueueOutcome.Completed && row.InvocationCount == 1)).IsTrue();
		await Assert.That(System.Text.Json.JsonSerializer.Serialize(recorder.Recent()).Contains("secret", StringComparison.Ordinal)).IsFalse();
	}

	[Test]
	public async Task RejectedAndCancelledWorkHaveNoExecutionDuration()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var scheduled = Substitute.For<IScheduler>();
		await using var queue = Create(recorder, capacity: 1, scheduled: scheduled);
		var state = ParserState.Empty with { Executor = new DBRef(2, 1), CurrentEvaluation = new DBAttribute(new DBRef(2, 1), "ACTION") };
		var first = await queue.WriteCommandList(MarkupText.Plain("private body"), state, TimeSpan.FromHours(1));
		await Assert.That(first.Accepted).IsTrue();
		var snapshot = queue.GetQueueEntry(first.Pid!.Value)!;
		await Assert.That(snapshot.SourceAttribute).IsEqualTo("ACTION");
		await Assert.That(snapshot.StartedAt).IsNull();
		var rejected = await queue.WriteCommandList(MarkupText.Plain("other secret"), state);
		await Assert.That(rejected.Accepted).IsFalse();
		await queue.HaltByPid(first.Pid.Value);
		await HistoryCount(recorder, 2);
		await Assert.That(recorder.Recent().All(row => row.StartedAt is null && row.ExecutionDuration is null)).IsTrue();
		await Assert.That(recorder.Recent().Select(row => row.Outcome).ToHashSet().SetEquals([QueueOutcome.GlobalLimit, QueueOutcome.Cancelled])).IsTrue();
	}

	[Test]
	public async Task ExceptionsProduceClosedOutcomesWithoutExceptionText()
	{
		var recorder = new QueueDiagnosticsRecorder();
		// The exception contract must not race a 20ms scheduling deadline on a loaded runner.
		await using var queue = Create(recorder, milliseconds: 30000);
		await queue.EnqueueWork(() => throw new InvalidOperationException("sensitive exception"), "throw", "enqueue");
		await HistoryCount(recorder, 1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.Failed);
		await Assert.That(System.Text.Json.JsonSerializer.Serialize(recorder.Recent()).Contains("sensitive", StringComparison.Ordinal)).IsFalse();
	}

	[Test]
	public async Task ExecutionLimitsProduceClosedOutcomes()
	{
		var recorder = new QueueDiagnosticsRecorder();
		await using var queue = Create(recorder, milliseconds: 20);
		await queue.EnqueueWork(async () => { await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionBudget.CurrentToken); return null; }, "wait", "enqueue");
		await HistoryCount(recorder, 1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.ExecutionLimit);
	}

	[Test]
	public async Task ShutdownWithoutAConsumerRecordsPendingCancellationExactlyOnce()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var queue = Create(recorder, scheduled: Substitute.For<IScheduler>());
		var result = await queue.WriteCommandList(MarkupText.Plain("private"), ParserState.Empty with { Executor = new DBRef(2, 1) }, TimeSpan.FromHours(1));
		await Assert.That(result.Accepted).IsTrue();
		await queue.DisposeAsync();
		await Assert.That(recorder.Recent().Count).IsEqualTo(1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.Cancelled);
		await Assert.That(recorder.Recent().Single().StartedAt).IsNull();
	}
}
