using Mediator;
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
	private static Scheduler Create(IMUSHCodeParser? parser = null) => new(parser ?? Substitute.For<IMUSHCodeParser>(),
		Substitute.For<IConnectionService>(), Substitute.For<ISchedulerFactory>(), Substitute.For<IAttributeService>(),
		Substitute.For<IMediator>(), NullLogger<Scheduler>.Instance);

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
		state.Registers.TryPeek(out var registers);
		registers!["CHECK"] = MarkupText.Plain("retained");
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
