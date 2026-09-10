using Mediator;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
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
		IMUSHCodeParser? parser = null, IScheduler? scheduled = null, IMediator? mediator = null)
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config with { Limit = config.Limit with { GlobalQueueLimit = capacity, PlayerQueueLimit = capacity, QueueEntryCpuTime = milliseconds } });
		var factory = Substitute.For<ISchedulerFactory>();
		if (scheduled is not null) factory.GetScheduler().Returns(scheduled);
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(), factory,
			Substitute.For<IAttributeService>(), mediator ?? QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance,
			options, diagnostics: recorder);
	}
	private static async Task HistoryCount(QueueDiagnosticsRecorder recorder, int count)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		while (recorder.Recent().Count < count) await Task.Delay(5, timeout.Token);
	}

	[Test]
	public Task RepresentativeQueueOverheadPreservesEveryInvocationAndSideEffect()
		=> RunRepresentativeQueue(16, 1, measure: false);

	[Test, Explicit, NotInParallel]
	public Task MeasureRepresentativeQueueOverhead()
		=> RunRepresentativeQueue(5000, 4, measure: true);

	private static async Task RunRepresentativeQueue(int count, int rounds, bool measure)
	{
		// Explicit measurements discard round zero, which includes process/JIT warm-up.
		for (var round = 0; round < rounds; round++)
			foreach (var mode in new[] { "disabled", "history", "profile" })
			{
				var recorder = mode == "disabled" ? null : new QueueDiagnosticsRecorder();
				if (mode == "profile") recorder!.StartProfile(new("benchmark", new DBRef(1, 1), new DBRef(1, 1)), TimeSpan.FromSeconds(60));
				using var telemetry = new TelemetryService(recorder is null ? [] : [recorder]);
				await using var queue = Create(recorder, capacity: (uint)count + 1);
				// Warm the queue and each observer mode before comparing their steady-state hot paths.
				for (var warmup = 0; warmup < (measure ? 300 : 0); warmup++)
					await queue.AdmitWork(() =>
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
				var allocatedBefore = measure ? GC.GetTotalAllocatedBytes() : 0;
				var elapsed = measure ? System.Diagnostics.Stopwatch.StartNew() : null;
				for (var i = 0; i < count; i++)
				{
					var expected = i;
					var admitted = await queue.AdmitWork(() =>
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
				elapsed?.Stop();
				if (measure) TestDiagnostics.WriteLine($"Queue diagnostics benchmark: round={round}, mode={mode}, entries={count}, elapsed_ms={elapsed!.Elapsed.TotalMilliseconds:F2}, allocated_bytes={GC.GetTotalAllocatedBytes() - allocatedBefore}");
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
			await queue.AdmitWork(() =>
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
	[Arguments(false)]
	[Arguments(true)]
	public async Task MissingDeferredTargetIsRecorded(bool attribute)
	{
		var recorder = new QueueDiagnosticsRecorder();
		var mediator = QueueAdmissionTests.TargetMediator();
		var missing = new DBRef(999, 1);
		mediator.Send(Arg.Is<GetObjectNodeQuery>(query => query.DBRef == missing), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(new None()));
		await using var queue = Create(recorder, mediator: mediator);
		var state = ParserState.Empty with { Executor = new DBRef(2, 1) };
		var target = new DbRefAttribute(missing, ["ACTION"]);
		var result = attribute
			? await queue.AdmitAsyncAttribute(() => ValueTask.FromResult(state), target, state.Executor)
			: await queue.AdmitCommandList(MarkupText.Plain("secret body"), state, target, 0);
		await Assert.That(result.Reason).IsEqualTo(SharpMUSH.Library.Models.SchedulerModels.QueueRejectionReason.InvalidTarget);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.InvalidTarget);
		await Assert.That(recorder.Recent().Single().Source).IsEqualTo(state.Executor);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task RejectedAndCancelledWorkHaveNoExecutionDuration()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var scheduled = Substitute.For<IScheduler>();
		await using var queue = Create(recorder, capacity: 1, scheduled: scheduled);
		var state = ParserState.Empty with { Executor = new DBRef(2, 1), CurrentEvaluation = new DBAttribute(new DBRef(2, 1), "ACTION") };
		var first = await queue.AdmitCommandList(MarkupText.Plain("private body"), state, TimeSpan.FromHours(1));
		await Assert.That(first.Accepted).IsTrue();
		var snapshot = queue.GetQueueEntry(first.Pid!.Value)!;
		await Assert.That(snapshot.SourceAttribute).IsEqualTo("ACTION");
		await Assert.That(snapshot.StartedAt).IsNull();
		var rejected = await queue.AdmitCommandList(MarkupText.Plain("other secret"), state);
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
		await queue.AdmitWork(() => throw new InvalidOperationException("sensitive exception"), "throw", "enqueue");
		await HistoryCount(recorder, 1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.Failed);
		await Assert.That(System.Text.Json.JsonSerializer.Serialize(recorder.Recent()).Contains("sensitive", StringComparison.Ordinal)).IsFalse();
	}

	[Test]
	public async Task ExecutionLimitsProduceClosedOutcomes()
	{
		var recorder = new QueueDiagnosticsRecorder();
		await using var queue = Create(recorder, milliseconds: 20);
		await queue.AdmitWork(async () => { await Task.Delay(Timeout.InfiniteTimeSpan, ExecutionBudget.CurrentToken); return null; }, "wait", "enqueue");
		await HistoryCount(recorder, 1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.ExecutionLimit);
	}

	[Test]
	public async Task CancelledTimeoutAccountingRetainsObservationUntilRecovery()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var count = 1;
		var block = true;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var mediator = QueueAdmissionTests.TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			new[] { new SharpAttribute("id", "key", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!)
			{ Value = MarkupText.Plain(count.ToString()) } }.ToAsyncEnumerable());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>())
			.Returns(async ValueTask<bool> (call) =>
			{
				if (block)
				{
					entered.TrySetResult();
					await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
				}
				count = int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
				return true;
			});
		await using var queue = Create(recorder, milliseconds: 30000, scheduled: Substitute.For<IScheduler>(), mediator: mediator);
		var result = await queue.AdmitCommandList(MarkupText.Plain("after recovery"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 1);
		using (var cancellation = new CancellationTokenSource())
		using (var budget = new ExecutionBudget(TimeSpan.FromSeconds(30), cancellation.Token))
		using (budget.Enter())
		{
			var release = queue.ReleaseScheduledWork(result.Pid!.Value, semaphoreTimeout: true).AsTask();
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await release);
		}
		await Assert.That(recorder.Recent().Count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(queue.GetQueueEntry(result.Pid!.Value)!.StartedAt).IsNull();
		block = false;
		using (await queue.EnterSemaphoreMutationAsync()) { }
		await HistoryCount(recorder, 1);
		var row = recorder.Recent().Single();
		await Assert.That(row.Pid).IsEqualTo(result.Pid);
		await Assert.That(row.Outcome).IsEqualTo(QueueOutcome.Completed);
		await Assert.That(row.StartedAt).IsNotNull();
		await Assert.That(row.ExecutionDuration).IsNotNull();
		await Assert.That(count).IsEqualTo(0);
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task SemaphoreBookkeepingNeverStartsBodyTiming(bool halt, bool repair)
	{
		var clock = new QueueDiagnosticsRecorderTests.Clock();
		var recorder = new QueueDiagnosticsRecorder(clock);
		var mediator = QueueAdmissionTests.TargetMediator();
		var count = 2;
		var accounting = false;
		var fail = repair;
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			new[] { new SharpAttribute("id", "key", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!)
			{ Value = MarkupText.Plain(count.ToString()) } }.ToAsyncEnumerable());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				if (accounting)
				{
					clock.Advance(TimeSpan.FromSeconds(5));
					if (fail) { fail = false; throw new IOException("retry accounting"); }
				}
				count = int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
				return ValueTask.FromResult(true);
			});
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var bodies = 0;
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ =>
		{
			Interlocked.Increment(ref bodies);
			clock.Advance(TimeSpan.FromSeconds(2));
			return ValueTask.FromResult<CallState?>(null);
		});
		await using var queue = Create(recorder, milliseconds: 30000, parser: parser,
			scheduled: Substitute.For<IScheduler>(), mediator: mediator);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("body"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), count, manageSemaphoreCount: true);
		await Assert.That(admission.Accepted).IsTrue();
		await Assert.That(count).IsEqualTo(3);
		accounting = true;
		clock.Advance(TimeSpan.FromSeconds(10));
		async Task Transition()
		{
			if (halt) await queue.HaltByPid(admission.Pid!.Value);
			else await queue.ReleaseScheduledWork(admission.Pid!.Value, semaphoreTimeout: true);
		}
		if (repair)
		{
			await Assert.ThrowsAsync<IOException>(Transition);
			await Assert.That(queue.GetQueueEntry(admission.Pid!.Value)!.StartedAt).IsNull();
			await Assert.That(recorder.Recent().Count).IsEqualTo(0);
			clock.Advance(TimeSpan.FromSeconds(10));
		}
		await Transition();
		await HistoryCount(recorder, 1);
		var row = recorder.Recent().Single();
		await Assert.That(row.Outcome).IsEqualTo(halt ? QueueOutcome.Cancelled : QueueOutcome.Completed);
		await Assert.That(row.WaitDuration).IsEqualTo(TimeSpan.FromSeconds(repair ? 30 : 15));
		await Assert.That(row.StartedAt).IsEqualTo(halt ? (DateTimeOffset?)null : DateTimeOffset.UnixEpoch.AddSeconds(repair ? 30 : 15));
		await Assert.That(row.ExecutionDuration).IsEqualTo(halt ? (TimeSpan?)null : TimeSpan.FromSeconds(2));
		await Assert.That(bodies).IsEqualTo(halt ? 0 : 1);
		await Assert.That(count).IsEqualTo(2);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task ShutdownWithoutAConsumerRecordsPendingCancellationExactlyOnce()
	{
		var recorder = new QueueDiagnosticsRecorder();
		var queue = Create(recorder, scheduled: Substitute.For<IScheduler>());
		var result = await queue.AdmitCommandList(MarkupText.Plain("private"), ParserState.Empty with { Executor = new DBRef(2, 1) }, TimeSpan.FromHours(1));
		await Assert.That(result.Accepted).IsTrue();
		await queue.DisposeAsync();
		await Assert.That(recorder.Recent().Count).IsEqualTo(1);
		await Assert.That(recorder.Recent().Single().Outcome).IsEqualTo(QueueOutcome.Cancelled);
		await Assert.That(recorder.Recent().Single().StartedAt).IsNull();
	}
}
