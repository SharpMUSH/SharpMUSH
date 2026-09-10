using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using Quartz.Impl.Matchers;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class SemaphoreInspectionCancellationTests
{
	[Test]
	[Arguments("object", "keys", false)]
	[Arguments("pid", "keys", false)]
	[Arguments("attribute", "keys", false)]
	[Arguments("object", "trigger", false)]
	[Arguments("pid", "trigger", false)]
	[Arguments("attribute", "trigger", false)]
	[Arguments("object", "job", false)]
	[Arguments("pid", "job", false)]
	[Arguments("attribute", "job", false)]
	[Arguments("object", "keys", true)]
	[Arguments("pid", "keys", true)]
	[Arguments("attribute", "keys", true)]
	[Arguments("object", "trigger", true)]
	[Arguments("pid", "trigger", true)]
	[Arguments("attribute", "trigger", true)]
	[Arguments("object", "job", true)]
	[Arguments("pid", "job", true)]
	[Arguments("attribute", "job", true)]
	public async Task SemaphoreInspectionHonorsCancellation(string selector, string stage, bool enumerationCancellation)
	{
		var scheduler = Substitute.For<IScheduler>();
		var factory = Substitute.For<ISchedulerFactory>();
		factory.GetScheduler().Returns(scheduler);
		await using var queue = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			factory, Substitute.For<IAttributeService>(), Substitute.For<Mediator.IMediator>(), NullLogger<Scheduler>.Instance);
		var target = new DbRefAttribute(new DBRef(7), ["SEMAPHORE"]);
		var key = new TriggerKey("dbref:#7-1", $"{Scheduler.SemaphoreGroup}:{target}");
		var trigger = Substitute.For<ITrigger>();
		trigger.JobKey.Returns(new JobKey("inspection"));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		async Task Block(CancellationToken token)
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			entered.TrySetResult();
			await Task.Delay(Timeout.Infinite, linked.Token);
		}
		scheduler.GetTriggerKeys(Arg.Any<GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>())
			.Returns(async Task<IReadOnlyCollection<TriggerKey>> (call) =>
			{
				if (stage == "keys") await Block(call.Arg<CancellationToken>());
				return [key];
			});
		scheduler.GetTrigger(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>())
			.Returns(async Task<ITrigger?> (call) =>
			{
				if (stage == "trigger") await Block(call.Arg<CancellationToken>());
				return trigger;
			});
		scheduler.GetJobDetail(Arg.Any<JobKey>(), Arg.Any<CancellationToken>())
			.Returns(async Task<IJobDetail?> (call) => { await Block(call.Arg<CancellationToken>()); return null; });
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, enumerationCancellation ? CancellationToken.None : cancellation.Token);
		using var scope = budget.Enter();
		async Task Inspect()
		{
			var tasks = selector switch
			{
				"object" => queue.GetSemaphoreTasks(new DBRef(7)),
				"pid" => queue.GetSemaphoreTasks(1L),
				_ => queue.GetSemaphoreTasks(target)
			};
			await foreach (var _ in tasks.WithCancellation(enumerationCancellation ? cancellation.Token : CancellationToken.None)) { }
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
