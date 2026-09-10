using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class LegacyNotifyHaltTests
{
	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	public async Task HaltRetriesRetainedNotificationCleanup(bool shutdown, bool lostAcknowledgement)
	{
		var fail = true;
		var count = 3;
		var writes = 0;
		var deletions = 0;
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
		scheduler.GetTriggerKeys(Arg.Any<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyCollection<TriggerKey>>(stored is null ? [] : [stored.Key]));
		scheduler.GetTrigger(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(stored));
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => { stored = null; return Task.FromResult(true); });
		scheduler.DeleteJob(Arg.Any<JobKey>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			if (fail) throw new IOException("delete failed");
			Interlocked.Increment(ref deletions);
			entered.TrySetResult();
			await finish.Task.WaitAsync(call.Arg<CancellationToken>());
			return jobs.Remove(call.Arg<JobKey>());
		});
		var mediator = QueueAdmissionTests.TargetMediator();
		mediator.CreateStream(Arg.Any<GetAttributeQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
				new[] { new SharpAttribute("", "", "SEMAPHORE", [], null, "SEMAPHORE", null!, null!, null!) { Value = MarkupText.Plain(count.ToString()) } }.ToAsyncEnumerable());
		mediator.Send(Arg.Any<SetAttributeCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
		{
			count = int.Parse(call.Arg<SetAttributeCommand>().Value.ToPlainText());
			writes++;
			if (lostAcknowledgement && writes == 1) throw new IOException("counter committed without acknowledgement");
			return ValueTask.FromResult(true);
		});
		var factory = Substitute.For<ISchedulerFactory>(); factory.GetScheduler().Returns(scheduler);
		var parser = Substitute.For<IMUSHCodeParser>();
		var queue = new Scheduler(parser, Substitute.For<IConnectionService>(), factory, Substitute.For<IAttributeService>(), mediator, NullLogger<Scheduler>.Instance);
		var disposed = false;
		try
		{
			var target = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
			var admitted = await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty, target, 0);
			await Assert.ThrowsAsync<IOException>(async () => await queue.NotifyCounted(target, 1));
			await Assert.ThrowsAsync<IOException>(async () => await queue.HaltByPid(admitted.Pid!.Value));
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
			await Assert.That(writes).IsEqualTo(0);
			fail = false;
			if (lostAcknowledgement)
			{
				finish.TrySetResult();
				await Assert.ThrowsAsync<IOException>(async () => await queue.HaltByPid(admitted.Pid!.Value));
				await Assert.That(count).IsEqualTo(2);
				await Assert.That(writes).IsEqualTo(1);
				await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
				// A compatibility notifier may run without the mutation lease. It must
				// not consume a cleanup claim protected by uncertain Halt accounting.
				var notification = await queue.NotifyCounted(target, count);
				await Assert.That(notification.Count).IsEqualTo(0);
				await queue.HaltByPid(admitted.Pid!.Value);
				var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				await queue.AdmitWork(() => { drained.TrySetResult(); return ValueTask.FromResult<CallState?>(null); }, "sentinel", "test");
				await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
				await Assert.That(count).IsEqualTo(2);
				await Assert.That(writes).IsEqualTo(1);
				await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupText>());
				return;
			}
			var halt = queue.HaltByPid(admitted.Pid!.Value).AsTask();
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			if (shutdown)
			{
				await queue.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
				disposed = true;
				await Assert.ThrowsAsync<OperationCanceledException>(async () => await halt);
			}
			else
			{
				var notify = queue.NotifyCounted(target, count).AsTask();
				await Assert.That(notify.IsCompleted).IsFalse();
				finish.TrySetResult();
				await halt;
				await notify;
				await Assert.That(count).IsEqualTo(2);
				await Assert.That(writes).IsEqualTo(1);
				await Assert.That(deletions).IsEqualTo(1);
				await Assert.That(jobs.Count).IsEqualTo(0);
				await Assert.That(await queue.HaltByPid(admitted.Pid!.Value)).IsFalse();
			}
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(0);
			await parser.DidNotReceive().CommandListParse(Arg.Any<MarkupText>());
		}
		finally { if (!disposed) await queue.DisposeAsync(); }
	}

	[Test]
	public async Task NotificationPreservesFiniteParentBudget()
	{
		TimeSpan remaining = default;
		var scheduler = Substitute.For<IScheduler>();
		scheduler.GetTriggerKeys(Arg.Any<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>()).Returns(_ =>
		{
			remaining = ExecutionBudget.Current!.Remaining;
			return Task.FromResult<IReadOnlyCollection<TriggerKey>>([]);
		});
		var factory = Substitute.For<ISchedulerFactory>(); factory.GetScheduler().Returns(scheduler);
		await using var queue = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(), factory, Substitute.For<IAttributeService>(), QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance);
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		await queue.NotifyCounted(new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]), 0);
		await Assert.That(remaining > TimeSpan.FromSeconds(1)).IsTrue();
		await Assert.That(remaining <= budget.Remaining + TimeSpan.FromSeconds(1)).IsTrue();
	}
}
