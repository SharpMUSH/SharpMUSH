using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// <see cref="QueueSettler"/> against a scheduler of its own, whose timers never fire: the test releases the
/// <c>@wait</c> itself, at the point it chooses.
/// </summary>
public class QueueSettlerTests
{
	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now;
	}

	private static Scheduler Create(IMUSHCodeParser parser)
		=> new(parser, Substitute.For<IConnectionService>(), SchedulerFactory(),
			Substitute.For<IAttributeService>(), QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance);

	private static ISchedulerFactory SchedulerFactory()
	{
		var factory = Substitute.For<ISchedulerFactory>();
		var quartz = Substitute.For<IScheduler>();
		factory.GetScheduler().Returns(quartz);
		return factory;
	}

	private static async Task Barrier(Scheduler queue)
	{
		var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var admission = await queue.AdmitWork(() =>
		{
			reached.TrySetResult();
			return ValueTask.FromResult<CallState?>(null);
		}, "test-barrier", Scheduler.EnqueueGroup);
		if (!admission.Accepted) throw new InvalidOperationException($"The barrier was refused: {admission.Reason}");
		await reached.Task;
	}

	[Test]
	[Timeout(30_000)]
	public async Task WaitsForAWaitReleasedAfterItsBarrierUntilItFinishes(CancellationToken _)
	{
		var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		parser.CommandListParse(Arg.Any<MString>()).Returns(_ => FollowUp());
		async ValueTask<CallState?> FollowUp()
		{
			started.TrySetResult();
			await gate.Task;
			return null;
		}

		await using var queue = Create(parser);
		var wait = await queue.AdmitCommandList(MarkupText.Plain("think follow-up"), ParserState.Empty, TimeSpan.Zero);
		var pid = wait.Pid ?? throw new InvalidOperationException($"The @wait was refused: {wait.Reason}");

		var barriers = 0;
		var secondBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var settler = new QueueSettler(async () =>
		{
			var n = ++barriers;
			if (n == 2) secondBarrier.TrySetResult();
			await Barrier(queue);
			if (n != 1) return;
			// The first barrier has finished; the @wait's timer fires now, before the settler reads the scheduler.
			await queue.ReleaseScheduledWork(pid);
			await started.Task;
		}, queue.DelaysForTests, new FixedClock(DateTimeOffset.UtcNow));

		var settle = settler.SettleAsync();
		try
		{
			await Task.WhenAny(settle, secondBarrier.Task);
			// Settling read the scheduler after the release and went back to the queue, instead of returning.
			await Assert.That(secondBarrier.Task.IsCompleted).IsTrue();
			// The follow-up is still running, so the barrier behind it, and the settling, cannot finish.
			await Assert.That(settle.IsCompleted).IsFalse();
			await Assert.That(queue.DelaysForTests()).IsEqualTo(((DateTimeOffset?)null, true));
		}
		finally { gate.TrySetResult(); }
		await settle;
		await Assert.That(barriers).IsEqualTo(2);
		await Assert.That(queue.DelaysForTests()).IsEqualTo(((DateTimeOffset?)null, false));
	}

	[Test]
	public async Task LeavesAWaitDueLaterForTheTest()
	{
		await using var queue = Create(Substitute.For<IMUSHCodeParser>());
		await queue.AdmitCommandList(MarkupText.Plain("think later"), ParserState.Empty, TimeSpan.FromHours(1));
		var barriers = 0;
		var settler = new QueueSettler(async () =>
		{
			barriers++;
			await Barrier(queue);
		}, queue.DelaysForTests, new FixedClock(DateTimeOffset.UtcNow));

		await settler.SettleAsync();
		await Assert.That(barriers).IsEqualTo(1);
		await Assert.That(queue.DelaysForTests().NextDue).IsNotNull();
	}
}
