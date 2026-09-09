using Mediator;
using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class QueuePauseTests
{
	private static Scheduler Create(IMUSHCodeParser? parser = null, IScheduler? scheduler = null, IMediator? mediator = null)
	{
		var factory = Substitute.For<ISchedulerFactory>();
		if (scheduler is not null) factory.GetScheduler().Returns(scheduler);
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(), factory,
			Substitute.For<IAttributeService>(), mediator ?? QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance);
	}

	[Test]
	[Arguments("pause")]
	[Arguments("resume")]
	[Arguments("retime")]
	public async Task PartialSemaphoreCleanupBlocksTimerChangesUntilRecovery(string operation)
	{
		var scheduler = Substitute.For<IScheduler>();
		var retained = new HashSet<TriggerKey>();
		var schedules = 0;
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				schedules++;
				retained.Add(call.Arg<ITrigger>().Key);
				return Task.FromResult(DateTimeOffset.UtcNow);
			});
		await using var queue = Create(scheduler: scheduler);
		var target = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		var first = await queue.AdmitCommandList(MarkupText.Plain("first"), ParserState.Empty, target, 0);
		var second = await queue.AdmitCommandList(MarkupText.Plain("second"), ParserState.Empty, target, 0);
		if (operation == "resume") await queue.PausePending(first.Pid!.Value, "before cleanup");
		var fail = true;
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var key = call.Arg<TriggerKey>();
			if (fail && key.Name == $"dbref:-{second.Pid}") throw new IOException("second cleanup failed");
			return Task.FromResult(retained.Remove(key));
		});
		using (await queue.EnterSemaphoreMutationAsync())
			await Assert.ThrowsAsync<IOException>(async () => await queue.ApplySemaphoreCommandAsync(target, null, true,
				_ => ValueTask.CompletedTask, () => ValueTask.FromException<bool>(new Exception("confirmed write"))));
		await Assert.That(retained.Count).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(2);
		try
		{
			switch (operation)
			{
				case "pause": await Assert.That(await queue.PausePending(first.Pid!.Value, "during repair")).IsEqualTo(QueueControlResult.NotPending); break;
				case "resume": await Assert.That(await queue.ResumePending(first.Pid!.Value)).IsEqualTo(QueueControlResult.NotPending); break;
				case "retime": await queue.RescheduleSemaphoreTask(first.Pid!.Value, TimeSpan.FromHours(1)); break;
			}
			await Assert.That(schedules).IsEqualTo(2);
		}
		finally
		{
			fail = false;
			using (await queue.EnterSemaphoreMutationAsync()) { }
		}
		await Assert.That(retained.Count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That(queue.GetQueueEntries().Count).IsEqualTo(0);
	}

	[Test]
	[Arguments("notify")]
	[Arguments("drain")]
	[Arguments("halt")]
	public async Task LegacySemaphoreCleanupFailureRetainsInspectablePid(string operation)
	{
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(scheduler: scheduler);
		var target = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think waiting"), ParserState.Empty, target, 0);
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromException<bool>(new IOException("Quartz unavailable")));
		async Task Change()
		{
			switch (operation)
			{
				case "notify": await queue.NotifyCounted(target, 1); break;
				case "drain": await queue.DrainCounted(target); break;
				case "halt": await queue.HaltByPid(admission.Pid!.Value); break;
			}
		}
		await Assert.ThrowsAsync<IOException>(Change);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(queue.GetQueueEntries().Single().Pid).IsEqualTo(admission.Pid!.Value);
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(true);
		await Change();
		if (operation != "notify") await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task OverflowingDelayDoesNotReserveAQueueEntry()
	{
		await using var queue = Create(scheduler: Substitute.For<IScheduler>());
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
			await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty, TimeSpan.MaxValue));
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That(queue.GetQueueEntries().Count).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task UncertainDeferredCleanupCannotBePausedOrResumed(bool semaphore)
	{
		var unavailable = true;
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromException<DateTimeOffset>(new InvalidOperationException("Lost schedule acknowledgement")));
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			if (unavailable) throw new InvalidOperationException("Cleanup unavailable");
			return true;
		});
		await using var queue = Create(scheduler: scheduler);
		if (semaphore)
			await Assert.ThrowsAsync<AggregateException>(async () =>
				await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty,
					new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.FromHours(1)));
		else
			await Assert.ThrowsAsync<InvalidOperationException>(async () =>
				await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty, TimeSpan.FromHours(1)));
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(await queue.PausePending(1, "hold")).IsEqualTo(QueueControlResult.NotPending);
		await Assert.That(await queue.ResumePending(1)).IsEqualTo(QueueControlResult.NotPending);
		unavailable = false;
		await queue.HaltByPid(1);
		await Assert.That(queue.GetQueueEntry(1)).IsNull();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ResumeIdentityReadsHonorCancellation(bool semaphore)
	{
		var mediator = QueueAdmissionTests.TargetMediator();
		await using var queue = Create(mediator: mediator);
		var state = ParserState.Empty with { Executor = new DBRef(7, 1) };
		var job = semaphore
			? await queue.AdmitCommandList(MarkupText.Plain("think retained"), state, new DbRefAttribute(new DBRef(8, 1), ["SEMAPHORE"]), 1)
			: await queue.AdmitCommandList(MarkupText.Plain("think retained"), state, TimeSpan.FromHours(1));
		await queue.PausePending(job.Pid!.Value, "hold");
		var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == new DBRef(semaphore ? 8 : 7, 1)), Arg.Any<CancellationToken>())
			.Returns(async ValueTask<AnyOptionalSharpObject> (call) =>
			{
				read.TrySetResult();
				using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Arg<CancellationToken>(), release.Token);
				await Task.Delay(Timeout.Infinite, linked.Token);
				return (AnyOptionalSharpObject)new None();
			});
		try
		{
			using var cancellation = new CancellationTokenSource();
			using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
			using var scope = budget.Enter();
			var resume = queue.ResumePending(job.Pid.Value).AsTask();
			await read.Task.WaitAsync(TimeSpan.FromSeconds(5));
			cancellation.Cancel();
			await Assert.That(async () => await resume.WaitAsync(TimeSpan.FromSeconds(1))).Throws<OperationCanceledException>();
		}
		finally { release.Cancel(); }
		await Assert.That(await queue.PausePending(999, "lease released")).IsEqualTo(QueueControlResult.NotFound);
	}

	[Test]
	public async Task ReplacementTriggerAndLedgerShareOneDeadline()
	{
		var scheduler = Substitute.For<IScheduler>();
		ITrigger? latest = null;
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>())
			.Returns(call => { latest = call.Arg<ITrigger>(); return Task.FromResult(latest.StartTimeUtc); });
		await using var queue = Create(scheduler: scheduler);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think later"), ParserState.Empty, TimeSpan.FromHours(1));
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(async _ => { await Task.Delay(150); return true; });
		await queue.RescheduleSemaphoreTask(job.Pid!.Value, TimeSpan.FromSeconds(10));
		await queue.PausePending(job.Pid.Value, "compare deadlines");
		var remaining = queue.GetQueueEntry(job.Pid.Value)!.RemainingDelay!.Value;
		await Assert.That(Math.Abs((latest!.StartTimeUtc - DateTimeOffset.UtcNow - remaining).TotalMilliseconds) < 75).IsTrue();
	}

	[Test]
	public async Task QuartzDelayedJobPreservesItsScheduleGeneration()
	{
		var queue = Substitute.For<ITaskScheduler>();
		var context = Substitute.For<IJobExecutionContext>();
		context.Trigger.Returns(TriggerBuilder.Create().WithIdentity("dbref:10-42", "delay:10").Build());
		context.MergedJobDataMap.Returns(new JobDataMap { ["Generation"] = 7L });
		await new DelayedTask(queue).Execute(context);
		await queue.Received(1).ReleaseScheduledWork(42, false, 7);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ManagedCommandsDoNotConsumeAlreadyNotifiedPausedWaiters(bool drainAgain)
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		ParserState? captured = null;
		parser.FromState(Arg.Any<ParserState>()).Returns(call => { captured = call.Arg<ParserState>(); return parser; });
		var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { ran.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser);
		var semaphore = new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think once"), ParserState.Empty, semaphore, 1);
		await queue.PausePending(job.Pid!.Value, "hold");
		var count = 1;
		ValueTask Persist(int selected) { count -= selected; return ValueTask.CompletedTask; }
		using (await queue.EnterSemaphoreMutationAsync())
		{
			await Assert.That(await queue.ApplySemaphoreCommandAsync(semaphore, 1, false, Persist,
				() => ValueTask.FromResult(false), new() { ["signal"] = MarkupText.Plain("retained") })).IsEqualTo(1);
			await Assert.That(await queue.ApplySemaphoreCommandAsync(semaphore, null, drainAgain, Persist,
				() => ValueTask.FromResult(false))).IsEqualTo(0);
		}
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(ran.Task.IsCompleted).IsFalse();
		await queue.ResumePending(job.Pid.Value);
		await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
		captured!.Registers.TryPeek(out var registers);
		await Assert.That(registers!["SIGNAL"].ToPlainText()).IsEqualTo("retained");
	}

	[Test]
	public async Task ContendedDeferredTransitionHonorsExecutionDeadline()
	{
		var scheduler = Substitute.For<IScheduler>();
		var scheduling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			scheduling.TrySetResult();
			return release.Task;
		});
		await using var queue = Create(scheduler: scheduler);
		var pending = queue.AdmitCommandList(MarkupText.Plain("think later"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.FromHours(1)).AsTask();
		await scheduling.Task.WaitAsync(TimeSpan.FromSeconds(5));
		try
		{
			using var budget = ExecutionBudget.FromMilliseconds(30);
			using var scope = budget.Enter();
			await Assert.That(async () => await queue.PausePending(999, "blocked").AsTask().WaitAsync(TimeSpan.FromSeconds(1)))
				.Throws<OperationCanceledException>();
		}
		finally
		{
			release.TrySetResult(DateTimeOffset.UtcNow);
			await pending;
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CountedDrainOnlyRemovesPausedWorkStillWaitingForNotification(bool notified)
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ =>
		{ executed.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser);
		var semaphore = new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty, semaphore, 1);
		await queue.PausePending(job.Pid!.Value, "hold");
		if (notified) await queue.Notify(semaphore, 1);
		await Assert.That(await queue.DrainCounted(semaphore)).IsEqualTo(notified ? 0 : 1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(notified ? 1 : 0);
		await Assert.That(executed.Task.IsCompleted).IsFalse();
		if (notified)
		{
			await queue.ResumePending(job.Pid.Value);
			await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task HaltingReleasedPausedSemaphoreAccountsOnlyOutstandingTimeout(bool managed, bool timeout)
	{
		var count = managed ? 0 : 1;
		var mediator = QueueAdmissionTests.CountingMediator(() => count, value => count = value);
		var parser = Substitute.For<IMUSHCodeParser>();
		await using var queue = Create(parser, mediator: mediator);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10, 1), ["SEMAPHORE"]), 1, TimeSpan.FromHours(1), manageSemaphoreCount: managed);
		await queue.PausePending(job.Pid!.Value, "hold");
		await Assert.That((await queue.ReleaseScheduledWork(job.Pid.Value, semaphoreTimeout: timeout)).Accepted).IsTrue();
		if (!timeout) count--; // Notification command has already persisted its counter.
		await Assert.That(count).IsEqualTo(timeout && !managed ? 1 : 0);
		await Assert.That(await queue.HaltByPid(job.Pid.Value)).IsTrue();
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await parser.DidNotReceiveWithAnyArgs().CommandListParse(default!);
	}

	[Test]
	public async Task ObsoletePausedTimerCannotDecrementTheManagedSemaphore()
	{
		var count = 0;
		var mediator = QueueAdmissionTests.CountingMediator(() => count, value => count = value);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ =>
		{ ran.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser, mediator: mediator);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think once"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10, 1), ["SEMAPHORE"]), 0, TimeSpan.FromHours(1), manageSemaphoreCount: true);
		await queue.PausePending(job.Pid!.Value, "hold");
		await Assert.That((await queue.ReleaseScheduledWork(job.Pid.Value, semaphoreTimeout: true, generation: 0)).Reason)
			.IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(count).IsEqualTo(1);
		await queue.ResumePending(job.Pid.Value);
		await Assert.That((await queue.ReleaseScheduledWork(job.Pid.Value, semaphoreTimeout: true, generation: 0)).Reason)
			.IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(count).IsEqualTo(1);
		await queue.ReleaseScheduledWork(job.Pid.Value, semaphoreTimeout: true, generation: 2);
		await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(count).IsEqualTo(0);
	}

	[Test]
	public async Task ARepeatedReleaseCannotReplaceTheSignalHeldByAPause()
	{
		await using var queue = Create();
		var semaphore = new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think once"), ParserState.Empty, semaphore, 1);
		await queue.PausePending(job.Pid!.Value, "hold");
		await Assert.That((await queue.ReleaseScheduledWork(job.Pid.Value)).Accepted).IsTrue();
		await Assert.That((await queue.ReleaseScheduledWork(job.Pid.Value, semaphoreTimeout: true)).Reason)
			.IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(queue.GetQueueEntries().Single().ReleasePending).IsTrue();
	}

	[Test]
	public async Task RacingFirePauseResumeAndCancelNeverExecuteTwice()
	{
		for (var iteration = 0; iteration < 25; iteration++)
		{
			var parser = Substitute.For<IMUSHCodeParser>();
			parser.FromState(Arg.Any<ParserState>()).Returns(parser);
			var count = 0;
			parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ =>
			{ Interlocked.Increment(ref count); return ValueTask.FromResult<CallState?>(null); });
			var queue = Create(parser);
			var job = await queue.AdmitCommandList(MarkupText.Plain("think once"), ParserState.Empty, TimeSpan.FromHours(1));
			var pid = job.Pid!.Value;
			await Task.WhenAll(
				Task.Run(async () => { await queue.PausePending(pid, "race"); await queue.ResumePending(pid); }),
				Task.Run(async () => { await queue.ReleaseScheduledWork(pid, semaphoreTimeout: false, generation: 0); await queue.ReleaseScheduledWork(pid, semaphoreTimeout: false, generation: 2); }),
				Task.Run(async () => await queue.HaltByPid(pid)));
			await queue.DisposeAsync();
			await Assert.That(count).IsLessThanOrEqualTo(1);
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		}
	}

	[Test]
	public async Task InspectionNeverReturnsArbitraryInternalGroupText()
	{
		await using var queue = Create();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var job = await queue.AdmitWork(async () =>
		{ entered.SetResult(); await release.Task; return null; }, "private trigger", "private group payload");
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(queue.GetQueueEntry(job.Pid!.Value)!.Kind).IsEqualTo("other");
			await Assert.That(queue.GetQueueEntries().Single().Kind).IsEqualTo("other");
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ResumeRejectsMissingCapturedExecutorOrSemaphoreTarget(bool semaphore)
	{
		var mediator = QueueAdmissionTests.TargetMediator();
		await using var queue = Create(mediator: mediator);
		var state = ParserState.Empty with { Executor = new DBRef(7, 1) };
		var job = semaphore
			? await queue.AdmitCommandList(MarkupText.Plain("think retained"), state, new DbRefAttribute(new DBRef(8, 1), ["SEMAPHORE"]), 1)
			: await queue.AdmitCommandList(MarkupText.Plain("think retained"), state, TimeSpan.FromHours(1));
		await queue.PausePending(job.Pid!.Value, "hold");
		var missing = new DBRef(semaphore ? 8 : 7, 1);
		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == missing), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(new None()));
		await Assert.That(await queue.ResumePending(job.Pid.Value)).IsEqualTo(QueueControlResult.InvalidIdentity);
		await Assert.That(queue.GetQueueEntry(job.Pid.Value)!.State).IsEqualTo(QueueEntryState.Paused);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
	}

	[Test]
	public async Task PausedWaiterRetainsNotifyRegistersEvenWithoutAQuartzTrigger()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		ParserState? captured = null;
		parser.FromState(Arg.Any<ParserState>()).Returns(call => { captured = call.Arg<ParserState>(); return parser; });
		var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { ran.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser);
		var semaphore = new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think signal"), ParserState.Empty, semaphore, 1);
		await queue.PausePending(job.Pid!.Value, "hold");
		await Assert.That(await queue.ModifyQRegisters(semaphore, new() { ["signal"] = MarkupText.Plain("retained") })).IsTrue();
		await queue.Notify(semaphore, 1);
		await queue.ResumePending(job.Pid.Value);
		await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
		captured!.Registers.TryPeek(out var registers);
		await Assert.That(registers!["SIGNAL"].ToPlainText()).IsEqualTo("retained");
	}

	[Test]
	public async Task RealTimerStaysPausedPastItsDeadlineAndFiresOnceAfterResume()
	{
		var quartz = await new Quartz.Impl.StdSchedulerFactory(new System.Collections.Specialized.NameValueCollection
		{
			["quartz.scheduler.instanceName"] = "pause-" + Guid.NewGuid().ToString("N"),
			["quartz.threadPool.threadCount"] = "1"
		}).GetScheduler();
		await quartz.Start();
		try
		{
			var parser = Substitute.For<IMUSHCodeParser>();
			parser.FromState(Arg.Any<ParserState>()).Returns(parser);
			var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var count = 0;
			parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ =>
			{ Interlocked.Increment(ref count); ran.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
			await using var queue = Create(parser, quartz);
			var jobFactory = Substitute.For<Quartz.Spi.IJobFactory>();
			jobFactory.NewJob(Arg.Any<Quartz.Spi.TriggerFiredBundle>(), quartz).Returns(new DelayedTask(queue));
			quartz.JobFactory = jobFactory;
			var job = await queue.AdmitCommandList(MarkupText.Plain("think once"), ParserState.Empty, TimeSpan.FromSeconds(1));
			await Assert.That(await queue.PausePending(job.Pid!.Value, "hold")).IsEqualTo(QueueControlResult.Applied);
			await Task.Delay(1200);
			await Assert.That(count).IsEqualTo(0);
			await Assert.That(await queue.ResumePending(job.Pid.Value)).IsEqualTo(QueueControlResult.Applied);
			await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
			await Assert.That(count).IsEqualTo(1);
		}
		finally { await quartz.Shutdown(); }
	}

	[Test]
	public async Task PauseRetainsPendingPidAndQuota()
	{
		await using var queue = Create();
		var admitted = await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty, TimeSpan.FromHours(1));
		await Assert.That(admitted.Accepted).IsTrue();
		await Assert.That(await queue.PausePending(admitted.Pid!.Value, "Inspect timed system")).IsEqualTo(QueueControlResult.Applied);
		var paused = queue.GetQueueEntries().Single();
		await Assert.That(paused.Pid).IsEqualTo(admitted.Pid.Value);
		await Assert.That(paused.State).IsEqualTo(QueueEntryState.Paused);
		await Assert.That(paused.PauseReason).IsEqualTo("Inspect timed system");
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Task.Delay(25);
		await Assert.That(queue.GetQueueEntries().Single().RemainingDelay).IsEqualTo(paused.RemainingDelay);
		await Assert.That(await queue.PausePending(admitted.Pid.Value, "Another reason")).IsEqualTo(QueueControlResult.AlreadyInState);
	}

	[Test]
	public async Task OldTimerCannotReleasePausedOrRescheduledWorkAndContextSurvives()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		ParserState? captured = null;
		parser.FromState(Arg.Any<ParserState>()).Returns(call => { captured = call.Arg<ParserState>(); return parser; });
		var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var count = 0;
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(_ =>
		{
			Interlocked.Increment(ref count); ran.TrySetResult(); return ValueTask.FromResult<CallState?>(null);
		});
		await using var queue = Create(parser);
		var state = ParserState.Empty;
		state.Registers.Push(new Dictionary<string, MarkupText> { ["CHECK"] = MarkupText.Plain("retained") });
		var job = await queue.AdmitCommandList(MarkupText.Plain("think once"), state, TimeSpan.FromHours(1));
		var pid = job.Pid!.Value;
		await queue.PausePending(pid, "inspect");
		await Assert.That((await queue.ReleaseScheduledWork(pid, semaphoreTimeout: false, generation: 0)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(await queue.ResumePending(pid)).IsEqualTo(QueueControlResult.Applied);
		await Assert.That((await queue.ReleaseScheduledWork(pid, semaphoreTimeout: false, generation: 0)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await queue.ReleaseScheduledWork(pid, semaphoreTimeout: false, generation: 2);
		await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await queue.ReleaseScheduledWork(pid, semaphoreTimeout: false, generation: 2);
		await Assert.That(count).IsEqualTo(1);
		captured!.Registers.TryPeek(out var capturedRegisters);
		await Assert.That(capturedRegisters!["CHECK"].ToPlainText()).IsEqualTo("retained");
	}

	[Test]
	public async Task SemaphoreSignalIsHeldUntilResume()
	{
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupString.MarkupText>()).Returns(_ => { ran.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think notified"), ParserState.Empty, new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]), 1);
		await queue.PausePending(job.Pid!.Value, "inspect");
		await queue.Notify(new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]), 1);
		await Assert.That(ran.Task.IsCompleted).IsFalse();
		await Assert.That(queue.GetQueueEntries().Single().ReleasePending).IsTrue();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(await queue.ResumePending(job.Pid.Value)).IsEqualTo(QueueControlResult.Applied);
		await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task RunningWorkCannotBePaused()
	{
		await using var queue = Create();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var job = await queue.AdmitWork(async () => { entered.SetResult(); await release.Task; return null; }, "active", "test");
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Assert.That(queue.GetQueueEntries().Single().State).IsEqualTo(QueueEntryState.Running);
			await Assert.That(await queue.PausePending(job.Pid!.Value, "inspect")).IsEqualTo(QueueControlResult.NotPending);
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	public async Task CancelledPauseCannotBeResumedOrFired()
	{
		await using var queue = Create();
		var job = await queue.AdmitCommandList(MarkupText.Plain("think cancelled"), ParserState.Empty, TimeSpan.FromHours(1));
		await queue.PausePending(job.Pid!.Value, "inspect");
		await queue.HaltByPid(job.Pid.Value);
		await Assert.That(await queue.ResumePending(job.Pid.Value)).IsEqualTo(QueueControlResult.NotFound);
		await Assert.That((await queue.ReleaseScheduledWork(job.Pid.Value, semaphoreTimeout: false, generation: 0)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task FreshSchedulerHasNoPausedWork()
	{
		await using (var prior = Create())
		{
			var job = await prior.AdmitCommandList(MarkupText.Plain("think old process"), ParserState.Empty, TimeSpan.FromHours(1));
			await prior.PausePending(job.Pid!.Value, "inspect");
		}
		await using var fresh = Create();
		await Assert.That(fresh.GetQueueEntries().Count).IsEqualTo(0);
		await Assert.That(fresh.GetQueueUsage().Total).IsEqualTo(0);
	}
}
