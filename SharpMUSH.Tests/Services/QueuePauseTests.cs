using Mediator;
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
	private static long RequirePid(QueueAdmissionResult admission)
		=> admission.Pid ?? throw new InvalidOperationException($"Fixture admission rejected: {admission.Reason}");

	private static Scheduler Create(IMUSHCodeParser? parser = null, IScheduler? scheduler = null, IMediator? mediator = null)
	{
		var factory = Substitute.For<ISchedulerFactory>();
		if (scheduler is not null) factory.GetScheduler().Returns(scheduler);
		return new(parser ?? Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(), factory,
			Substitute.For<IAttributeService>(), mediator ?? QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance);
	}

	[Test]
	[Arguments(false, "delay")]
	[Arguments(true, "semaphore")]
	public async Task DeferredSnapshotDistinguishesNullableSemaphore(bool semaphore, string expectedKind)
	{
		await using var queue = Create(scheduler: Substitute.For<IScheduler>());
		var job = semaphore
			? await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty,
				new DbRefAttribute(new DBRef(8, 1), ["SEMAPHORE"]), 1)
			: await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty, TimeSpan.FromHours(1));
		await Assert.That(queue.GetQueueEntry(RequirePid(job))!.Kind).IsEqualTo(expectedKind);
		await Assert.That(queue.GetQueueEntries().Single().Kind).IsEqualTo(expectedKind);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HaltReconcilesCommittedCounterWithoutRetainingPausedWork(bool paused)
	{
		var count = 3;
		var writes = 0;
		var mediator = QueueAdmissionTests.TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
			new[] { new SharpAttribute("id", "key", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!)
			{ Value = MarkupText.Plain(count.ToString()) } }.ToAsyncEnumerable());
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			count = int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
			if (++writes == 1) throw new IOException("committed without acknowledgement");
			return ValueTask.FromResult(true);
		});
		var parser = Substitute.For<IMUSHCodeParser>();
		await using var queue = Create(parser, Substitute.For<IScheduler>(), mediator);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think never"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0);
		if (paused) await Assert.That(await queue.PausePending(admission.Pid!.Value, "hold")).IsEqualTo(QueueControlResult.Applied);
		await Assert.ThrowsAsync<IOException>(async () => await queue.HaltByPid(admission.Pid!.Value));
		await Assert.That(count).IsEqualTo(2);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(await queue.HaltByPid(admission.Pid!.Value)).IsTrue();
		await Assert.That(count).IsEqualTo(2);
		await Assert.That(writes).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That(queue.GetQueueEntry(admission.Pid!.Value)).IsNull();
		await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupText>());
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task NotifyRetriesJobDeletionAfterTriggerIsGone(bool canceled)
	{
		var fail = true;
		using var requestCancellation = new CancellationTokenSource();
		ITrigger? stored = null;
		var jobs = new HashSet<JobKey>();
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var job = call.Arg<IJobDetail>();
			stored = call.Arg<ITrigger>().GetTriggerBuilder().ForJob(job).Build();
			jobs.Add(job.Key);
			return Task.FromResult(DateTimeOffset.UtcNow);
		});
		scheduler.GetTrigger(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(stored));
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => { stored = null; if (fail && canceled) requestCancellation.Cancel(); return Task.FromResult(true); });
		scheduler.DeleteJob(Arg.Any<JobKey>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			call.Arg<CancellationToken>().ThrowIfCancellationRequested();
			if (fail) throw new IOException("delete failed");
			return Task.FromResult(jobs.Remove(call.Arg<JobKey>()));
		});
		var parser = Substitute.For<IMUSHCodeParser>(); parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = 0;
		var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { Interlocked.Increment(ref ran); executed.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser, scheduler);
		var target = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty, target, 0);
		async Task FirstAttempt()
		{
			using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, requestCancellation.Token);
			using var scope = budget.Enter();
			await queue.NotifyCounted(target, 1);
		}
		if (canceled) await Assert.ThrowsAsync<OperationCanceledException>(FirstAttempt);
		else await Assert.ThrowsAsync<IOException>(FirstAttempt);
		await Assert.That(requestCancellation.IsCancellationRequested).IsEqualTo(canceled);
		await Assert.That(stored).IsNull();
		await Assert.That(jobs.Count).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(ran).IsEqualTo(0);
		await Assert.That(await queue.PausePending(admission.Pid!.Value, "cleanup pending")).IsEqualTo(QueueControlResult.NotPending);
		await queue.RescheduleSemaphoreTask(admission.Pid!.Value, TimeSpan.FromHours(1));
		await Assert.That(stored).IsNull();
		await Assert.That((await queue.ReleaseScheduledWork(admission.Pid!.Value, true)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(ran).IsEqualTo(0);
		fail = false;
		await queue.NotifyCounted(target, 1);
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(jobs.Count).IsEqualTo(0);
		await Assert.That(ran).IsEqualTo(1);
	}

	[Test]
	public async Task DelayedReleaseRetainsWorkUntilTransportCleanupSucceeds()
	{
		var fail = true;
		var scheduler = Substitute.For<IScheduler>();
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ =>
			fail ? throw new InvalidOperationException("cleanup failed") : Task.FromResult(true));
		var parser = Substitute.For<IMUSHCodeParser>(); parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { ran.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser, scheduler);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty, TimeSpan.FromHours(1));
		await Assert.That(async () => await queue.ReleaseScheduledWork(RequirePid(job))).Throws<InvalidOperationException>();
		await Assert.That(ran.Task.IsCompleted).IsFalse();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		fail = false;
		await queue.ReleaseScheduledWork(RequirePid(job));
		await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	[Arguments(false, false, false)]
	[Arguments(true, false, false)]
	[Arguments(false, true, false)]
	[Arguments(true, true, false)]
	[Arguments(false, false, true)]
	[Arguments(true, false, true)]
	[Arguments(false, true, true)]
	[Arguments(true, true, true)]
	public async Task TimeoutCleanupFailureRetainsAccountedWork(bool paused, bool recoverWrite, bool failDelete)
	{
		var count = 0; var writes = 0; var writeFails = false; var cleanupFails = false;
		var mediator = QueueAdmissionTests.CountingMediator(() => count, value => { count = value; writes++; });
		mediator.Send(Arg.Any<SharpMUSH.Library.Commands.Database.SetAttributeCommand>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				if (writeFails) return ValueTask.FromResult(false);
				count = int.Parse(call.Arg<SharpMUSH.Library.Commands.Database.SetAttributeCommand>().Value.ToPlainText());
				writes++; return ValueTask.FromResult(true);
			});
		var triggers = new Dictionary<TriggerKey, ITrigger>(); var jobs = new HashSet<JobKey>();
		var scheduler = Substitute.For<IScheduler>();
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			var job = call.Arg<IJobDetail>(); var trigger = call.Arg<ITrigger>().GetTriggerBuilder().ForJob(job).Build();
			triggers[trigger.Key] = trigger; jobs.Add(job.Key); return Task.FromResult(DateTimeOffset.UtcNow);
		});
		scheduler.GetTrigger(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(triggers.GetValueOrDefault(call.Arg<TriggerKey>())));
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			if (cleanupFails && !failDelete) throw new InvalidOperationException("unschedule failed");
			return Task.FromResult(triggers.Remove(call.Arg<TriggerKey>()));
		});
		scheduler.DeleteJob(Arg.Any<JobKey>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			if (cleanupFails && failDelete) throw new InvalidOperationException("delete failed");
			return Task.FromResult(jobs.Remove(call.Arg<JobKey>()));
		});
		var parser = Substitute.For<IMUSHCodeParser>(); parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = 0; var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { Interlocked.Increment(ref ran); executed.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser, scheduler, mediator);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty,
			new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0, TimeSpan.FromHours(1), manageSemaphoreCount: true);
		if (paused) await queue.PausePending(RequirePid(job), "hold");
		writes = 0;
		if (recoverWrite)
		{
			writeFails = true;
			await Assert.That(async () => await queue.ReleaseScheduledWork(RequirePid(job), true)).Throws<InvalidOperationException>();
			writeFails = false;
		}
		cleanupFails = true;
		await Assert.That(async () => await queue.ReleaseScheduledWork(RequirePid(job), true)).Throws<InvalidOperationException>();
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(writes).IsEqualTo(1);
		await Assert.That(ran).IsEqualTo(0);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		if (paused) await Assert.That(await queue.ResumePending(RequirePid(job))).IsEqualTo(QueueControlResult.NotPending);
		cleanupFails = false;
		await queue.ReleaseScheduledWork(RequirePid(job), true);
		if (paused)
		{
			await Assert.That(ran).IsEqualTo(0);
			await Assert.That(await queue.ResumePending(RequirePid(job))).IsEqualTo(QueueControlResult.Applied);
		}
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		for (var i = 0; i < 100 && queue.GetQueueUsage().Total != 0; i++) await Task.Delay(10);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
		await Assert.That(writes).IsEqualTo(1);
		await Assert.That(ran).IsEqualTo(1);
		await Assert.That(triggers.Count).IsEqualTo(0);
		await Assert.That(jobs.Count).IsEqualTo(0);
	}

	[Test]
	public async Task StalledQuartzPauseHonorsBudgetAndReleasesTransitionLease()
	{
		var scheduler = Substitute.For<IScheduler>();
		await using var queue = Create(scheduler: scheduler);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty, TimeSpan.FromHours(1));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		scheduler.PauseTrigger(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Arg<CancellationToken>(), release.Token);
			entered.TrySetResult();
			await Task.Delay(Timeout.Infinite, linked.Token);
		});
		Task<QueueControlResult>? pause = null;
		try
		{
			using (var cancellation = new CancellationTokenSource())
			using (var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token))
			using (budget.Enter())
			{
				pause = queue.PausePending(RequirePid(job), "inspect").AsTask();
				await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
				cancellation.Cancel();
				await Assert.That(await pause.WaitAsync(TimeSpan.FromSeconds(1))).IsEqualTo(QueueControlResult.Applied);
			}
			await Assert.That(queue.GetQueueEntry(RequirePid(job))!.State).IsEqualTo(QueueEntryState.Paused);
			await Assert.That(await queue.PausePending(999, "lease released").AsTask().WaitAsync(TimeSpan.FromSeconds(1)))
				.IsEqualTo(QueueControlResult.NotFound);
		}
		finally
		{
			release.Cancel();
			if (pause is not null) await pause;
		}
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
	public async Task UncertainDeferredCleanupCannotBePausedResumedOrRetimed(bool semaphore)
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
		scheduler.ScheduleJob(Arg.Any<IJobDetail>(), Arg.Any<ITrigger>(), Arg.Any<CancellationToken>()).Returns(DateTimeOffset.UtcNow);
		scheduler.ClearReceivedCalls();
		await queue.RescheduleSemaphoreTask(1, TimeSpan.FromHours(2));
		await Assert.That(scheduler.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "ScheduleJob")).IsFalse();
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
		await queue.PausePending(RequirePid(job), "hold");
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
			var resume = queue.ResumePending(RequirePid(job)).AsTask();
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
		await queue.RescheduleSemaphoreTask(RequirePid(job), TimeSpan.FromSeconds(10));
		await queue.PausePending(RequirePid(job), "compare deadlines");
		var remaining = queue.GetQueueEntry(RequirePid(job))!.RemainingDelay!.Value;
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
		await queue.PausePending(RequirePid(job), "hold");
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
		await queue.ResumePending(RequirePid(job));
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
		await queue.PausePending(RequirePid(job), "hold");
		if (notified) await queue.Notify(semaphore, 1);
		await Assert.That(await queue.DrainCounted(semaphore)).IsEqualTo(notified ? 0 : 1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(notified ? 1 : 0);
		await Assert.That(executed.Task.IsCompleted).IsFalse();
		if (notified)
		{
			await queue.ResumePending(RequirePid(job));
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
		await queue.PausePending(RequirePid(job), "hold");
		await Assert.That((await queue.ReleaseScheduledWork(RequirePid(job), semaphoreTimeout: timeout)).Accepted).IsTrue();
		if (!timeout) count--; // Notification command has already persisted its counter.
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(await queue.HaltByPid(RequirePid(job))).IsTrue();
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
		await queue.PausePending(RequirePid(job), "hold");
		await Assert.That((await queue.ReleaseScheduledWork(RequirePid(job), semaphoreTimeout: true, generation: 0)).Reason)
			.IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(count).IsEqualTo(1);
		await queue.ResumePending(RequirePid(job));
		await Assert.That((await queue.ReleaseScheduledWork(RequirePid(job), semaphoreTimeout: true, generation: 0)).Reason)
			.IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(count).IsEqualTo(1);
		await queue.ReleaseScheduledWork(RequirePid(job), semaphoreTimeout: true, generation: 2);
		await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(count).IsEqualTo(0);
	}

	[Test]
	public async Task ARepeatedReleaseCannotReplaceTheSignalHeldByAPause()
	{
		await using var queue = Create();
		var semaphore = new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]);
		var job = await queue.AdmitCommandList(MarkupText.Plain("think once"), ParserState.Empty, semaphore, 1);
		await queue.PausePending(RequirePid(job), "hold");
		await Assert.That((await queue.ReleaseScheduledWork(RequirePid(job))).Accepted).IsTrue();
		await Assert.That((await queue.ReleaseScheduledWork(RequirePid(job), semaphoreTimeout: true)).Reason)
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
			var pid = RequirePid(job);
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
			await Assert.That(queue.GetQueueEntry(RequirePid(job))!.Kind).IsEqualTo("other");
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
		await queue.PausePending(RequirePid(job), "hold");
		var missing = new DBRef(semaphore ? 8 : 7, 1);
		mediator.Send(Arg.Is<GetObjectNodeQuery>(q => q.DBRef == missing), Arg.Any<CancellationToken>())
			.Returns(ValueTask.FromResult<AnyOptionalSharpObject>(new None()));
		await Assert.That(await queue.ResumePending(RequirePid(job))).IsEqualTo(QueueControlResult.InvalidIdentity);
		await Assert.That(queue.GetQueueEntry(RequirePid(job))!.State).IsEqualTo(QueueEntryState.Paused);
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
		await queue.PausePending(RequirePid(job), "hold");
		await Assert.That(await queue.ModifyQRegisters(semaphore, new() { ["signal"] = MarkupText.Plain("retained") })).IsTrue();
		await queue.Notify(semaphore, 1);
		await queue.ResumePending(RequirePid(job));
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
			await Assert.That(await queue.PausePending(RequirePid(job), "hold")).IsEqualTo(QueueControlResult.Applied);
			await Task.Delay(1200);
			await Assert.That(count).IsEqualTo(0);
			await Assert.That(await queue.ResumePending(RequirePid(job))).IsEqualTo(QueueControlResult.Applied);
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
		var pid = RequirePid(job);
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
		await queue.PausePending(RequirePid(job), "inspect");
		await queue.Notify(new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]), 1);
		await Assert.That(ran.Task.IsCompleted).IsFalse();
		await Assert.That(queue.GetQueueEntries().Single().ReleasePending).IsTrue();
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(await queue.ResumePending(RequirePid(job))).IsEqualTo(QueueControlResult.Applied);
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
			await Assert.That(await queue.PausePending(RequirePid(job), "inspect")).IsEqualTo(QueueControlResult.NotPending);
		}
		finally { release.TrySetResult(); }
	}

	[Test]
	public async Task CancelledPauseCannotBeResumedOrFired()
	{
		await using var queue = Create();
		var job = await queue.AdmitCommandList(MarkupText.Plain("think cancelled"), ParserState.Empty, TimeSpan.FromHours(1));
		await queue.PausePending(RequirePid(job), "inspect");
		await queue.HaltByPid(RequirePid(job));
		await Assert.That(await queue.ResumePending(RequirePid(job))).IsEqualTo(QueueControlResult.NotFound);
		await Assert.That((await queue.ReleaseScheduledWork(RequirePid(job), semaphoreTimeout: false, generation: 0)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task FreshSchedulerHasNoPausedWork()
	{
		await using (var prior = Create())
		{
			var job = await prior.AdmitCommandList(MarkupText.Plain("think old process"), ParserState.Empty, TimeSpan.FromHours(1));
			await prior.PausePending(RequirePid(job), "inspect");
		}
		await using var fresh = Create();
		await Assert.That(fresh.GetQueueEntries().Count).IsEqualTo(0);
		await Assert.That(fresh.GetQueueUsage().Total).IsEqualTo(0);
	}
}
