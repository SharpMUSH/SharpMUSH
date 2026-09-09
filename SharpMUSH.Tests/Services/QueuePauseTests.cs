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
		var job = await queue.WriteCommandList(MarkupText.Plain("think retained"), ParserState.Empty, semaphore, 1);
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
			var job = await queue.WriteCommandList(MarkupText.Plain("think once"), ParserState.Empty, TimeSpan.FromHours(1));
			var pid = job.Pid!.Value;
			await Task.WhenAll(
				Task.Run(async () => { await queue.PausePending(pid, "race"); await queue.ResumePending(pid); }),
				Task.Run(async () => { await queue.ReleaseScheduledWork(pid, generation: 0); await queue.ReleaseScheduledWork(pid, generation: 2); }),
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
		var job = await queue.EnqueueWork(async () =>
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
			? await queue.WriteCommandList(MarkupText.Plain("think retained"), state, new DbRefAttribute(new DBRef(8, 1), ["SEMAPHORE"]), 1)
			: await queue.WriteCommandList(MarkupText.Plain("think retained"), state, TimeSpan.FromHours(1));
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
		var job = await queue.WriteCommandList(MarkupText.Plain("think signal"), ParserState.Empty, semaphore, 1);
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
			var job = await queue.WriteCommandList(MarkupText.Plain("think once"), ParserState.Empty, TimeSpan.FromSeconds(1));
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
		var admitted = await queue.WriteCommandList(MarkupText.Plain("think retained"), ParserState.Empty, TimeSpan.FromHours(1));
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
		var job = await queue.WriteCommandList(MarkupText.Plain("think once"), state, TimeSpan.FromHours(1));
		var pid = job.Pid!.Value;
		await queue.PausePending(pid, "inspect");
		await Assert.That((await queue.ReleaseScheduledWork(pid, generation: 0)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(count).IsEqualTo(0);
		await Assert.That(await queue.ResumePending(pid)).IsEqualTo(QueueControlResult.Applied);
		await Assert.That((await queue.ReleaseScheduledWork(pid, generation: 0)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await queue.ReleaseScheduledWork(pid, generation: 2);
		await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await queue.ReleaseScheduledWork(pid, generation: 2);
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
		var job = await queue.WriteCommandList(MarkupText.Plain("think notified"), ParserState.Empty, new DbRefAttribute(new DBRef(50, 1), ["SEMAPHORE"]), 1);
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
		var job = await queue.EnqueueWork(async () => { entered.SetResult(); await release.Task; return null; }, "active", "test");
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
		var job = await queue.WriteCommandList(MarkupText.Plain("think cancelled"), ParserState.Empty, TimeSpan.FromHours(1));
		await queue.PausePending(job.Pid!.Value, "inspect");
		await queue.HaltByPid(job.Pid.Value);
		await Assert.That(await queue.ResumePending(job.Pid.Value)).IsEqualTo(QueueControlResult.NotFound);
		await Assert.That((await queue.ReleaseScheduledWork(job.Pid.Value, generation: 0)).Reason).IsEqualTo(QueueRejectionReason.AlreadyReleased);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
	}

	[Test]
	public async Task FreshSchedulerHasNoPausedWork()
	{
		await using (var prior = Create())
		{
			var job = await prior.WriteCommandList(MarkupText.Plain("think old process"), ParserState.Empty, TimeSpan.FromHours(1));
			await prior.PausePending(job.Pid!.Value, "inspect");
		}
		await using var fresh = Create();
		await Assert.That(fresh.GetQueueEntries().Count).IsEqualTo(0);
		await Assert.That(fresh.GetQueueUsage().Total).IsEqualTo(0);
	}
}
