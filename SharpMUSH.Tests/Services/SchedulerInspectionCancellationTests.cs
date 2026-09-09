using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using Quartz.Impl.Matchers;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class SchedulerInspectionCancellationTests
{
	[Test]
	public async Task TaskInspectionSkipsRecurringTriggersWithoutFinalFireTime()
	{
		var scheduler = Substitute.For<IScheduler>();
		var factory = Substitute.For<ISchedulerFactory>();
		factory.GetScheduler().Returns(scheduler);
		await using var queue = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			factory, Substitute.For<IAttributeService>(), Substitute.For<Mediator.IMediator>(), NullLogger<Scheduler>.Instance);
		var recurringKey = new TriggerKey("purge", "maintenance");
		var finiteKey = new TriggerKey("dbref:#7-1", "delay:#7");
		var recurring = Substitute.For<ITrigger>();
		recurring.Key.Returns(recurringKey);
		recurring.FinalFireTimeUtc.Returns((DateTimeOffset?)null);
		var finite = Substitute.For<ITrigger>();
		finite.Key.Returns(finiteKey);
		var due = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
		finite.FinalFireTimeUtc.Returns(due);
		scheduler.GetTriggerKeys(Arg.Any<GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>()).Returns(new[] { recurringKey, finiteKey });
		scheduler.GetTrigger(recurringKey, Arg.Any<CancellationToken>()).Returns(recurring);
		scheduler.GetTrigger(finiteKey, Arg.Any<CancellationToken>()).Returns(finite);
		var groups = await queue.GetAllTasks().ToArrayAsync();
		await Assert.That(groups.Length).IsEqualTo(1);
		await Assert.That(groups[0].Group).IsEqualTo(finiteKey.Group);
		await Assert.That(groups[0].Item2.Length).IsEqualTo(1);
		await Assert.That(groups[0].Item2[0].Item1).IsEqualTo(due);
	}

	[Test]
	[Arguments("all", false)]
	[Arguments("delay", false)]
	[Arguments("trigger", false)]
	[Arguments("all", true)]
	[Arguments("delay", true)]
	[Arguments("trigger", true)]
	public async Task TriggerInspectionHonorsCancellation(string stage, bool enumerationCancellation)
	{
		var scheduler = Substitute.For<IScheduler>();
		var factory = Substitute.For<ISchedulerFactory>();
		factory.GetScheduler().Returns(scheduler);
		await using var queue = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			factory, Substitute.For<IAttributeService>(), Substitute.For<Mediator.IMediator>(), NullLogger<Scheduler>.Instance);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		if (stage != "trigger")
		{
			scheduler.GetTriggerKeys(Arg.Any<GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>())
				.Returns(async Task<IReadOnlyCollection<TriggerKey>> (call) =>
				{
					using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Arg<CancellationToken>(), release.Token);
					entered.TrySetResult();
					await Task.Delay(Timeout.Infinite, linked.Token);
					return Array.Empty<TriggerKey>();
				});
		}
		if (stage == "trigger")
		{
			scheduler.GetTriggerKeys(Arg.Any<GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>())
				.Returns(new[] { new TriggerKey("inspection") });
			scheduler.GetTrigger(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>())
				.Returns(async Task<ITrigger?> (call) =>
				{
					using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Arg<CancellationToken>(), release.Token);
					entered.TrySetResult();
					await Task.Delay(Timeout.Infinite, linked.Token);
					return null;
				});
		}
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, enumerationCancellation ? CancellationToken.None : cancellation.Token);
		using var scope = budget.Enter();
		async Task Inspect()
		{
			var token = enumerationCancellation ? cancellation.Token : CancellationToken.None;
			if (stage == "delay") { await foreach (var _ in queue.GetDelayTasks(new DBRef(7)).WithCancellation(token)) { } }
			else { await foreach (var _ in queue.GetAllTasks().WithCancellation(token)) { } }
		}
		var inspect = Inspect();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			cancellation.Cancel();
			await Assert.That(async () => await inspect.WaitAsync(TimeSpan.FromSeconds(1))).Throws<OperationCanceledException>();
		}
		finally
		{
			release.Cancel();
			try { await inspect; } catch (OperationCanceledException) { }
		}
	}
}
