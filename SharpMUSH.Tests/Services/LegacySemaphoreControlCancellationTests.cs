using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using Quartz.Impl.Matchers;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class LegacySemaphoreControlCancellationTests
{
	[Test]
	[Arguments("notify")]
	[Arguments("drain")]
	[Arguments("registers")]
	[Arguments("retime")]
	public async Task LegacyQuartzControlHonorsExecutionCancellation(string operation)
	{
		var scheduler = Substitute.For<IScheduler>();
		var factory = Substitute.For<ISchedulerFactory>();
		factory.GetScheduler().Returns(scheduler);
		await using var queue = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			factory, Substitute.For<IAttributeService>(), Substitute.For<Mediator.IMediator>(), NullLogger<Scheduler>.Instance);
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		scheduler.GetTriggerKeys(Arg.Any<GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>())
			.Returns(async Task<IReadOnlyCollection<TriggerKey>> (call) =>
			{
				using var linked = CancellationTokenSource.CreateLinkedTokenSource(call.Arg<CancellationToken>(), cleanup.Token);
				entered.TrySetResult();
				await Task.Delay(Timeout.Infinite, linked.Token);
				return [];
			});
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
		using var scope = budget.Enter();
		var target = new DbRefAttribute(new DBRef(7), ["SEMAPHORE"]);
		async Task Invoke()
		{
			switch (operation)
			{
				case "notify": await queue.NotifyCounted(target, 1); break;
				case "drain": await queue.DrainCounted(target); break;
				case "registers": await queue.ModifyQRegisters(target, new() { ["a"] = MarkupText.Plain("value") }); break;
				case "retime": await queue.RescheduleSemaphoreTask(1, TimeSpan.FromHours(1)); break;
			}
		}
		var pending = Invoke();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			cancellation.Cancel();
			await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(1))).Throws<OperationCanceledException>();
		}
		finally
		{
			cleanup.Cancel();
			try { await pending; } catch (OperationCanceledException) { }
		}
	}
}
