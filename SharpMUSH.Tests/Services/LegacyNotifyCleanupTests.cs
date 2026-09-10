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

public class LegacyNotifyCleanupTests
{
	private static Scheduler Create(IMUSHCodeParser parser, IScheduler scheduler)
	{
		var factory = Substitute.For<ISchedulerFactory>(); factory.GetScheduler().Returns(scheduler);
		return new(parser, Substitute.For<IConnectionService>(), factory, Substitute.For<IAttributeService>(), QueueAdmissionTests.TargetMediator(), NullLogger<Scheduler>.Instance);
	}
	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task NotifyRetriesJobDeletionAfterTriggerIsGone(bool canceled, bool holdsSemaphoreLease)
	{
		var fail = true;
		var cleanupAttempts = 0;
		var retryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finishRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
		scheduler.GetTriggerKeys(Arg.Any<Quartz.Impl.Matchers.GroupMatcher<TriggerKey>>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<IReadOnlyCollection<TriggerKey>>(stored is null ? Array.Empty<TriggerKey>() : new[] { stored.Key }));
		scheduler.GetTrigger(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(stored));
		scheduler.UnscheduleJob(Arg.Any<TriggerKey>(), Arg.Any<CancellationToken>()).Returns(_ => { stored = null; return Task.FromResult(true); });
		scheduler.DeleteJob(Arg.Any<JobKey>(), Arg.Any<CancellationToken>()).Returns(async call =>
		{
			if (fail) throw canceled ? new OperationCanceledException() : new IOException("delete failed");
			if (Interlocked.Increment(ref cleanupAttempts) == 1)
			{
				retryEntered.TrySetResult();
				await finishRetry.Task.WaitAsync(call.Arg<CancellationToken>());
			}
			return jobs.Remove(call.Arg<JobKey>());
		});
		var parser = Substitute.For<IMUSHCodeParser>(); parser.FromState(Arg.Any<ParserState>()).Returns(parser);
		var ran = 0;
		var executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		parser.CommandListParse(Arg.Any<MarkupText>()).Returns(_ => { Interlocked.Increment(ref ran); executed.TrySetResult(); return ValueTask.FromResult<CallState?>(null); });
		await using var queue = Create(parser, scheduler);
		var target = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		var admission = await queue.AdmitCommandList(MarkupText.Plain("think retained"), ParserState.Empty, target, 0);
		using var heldLease = holdsSemaphoreLease ? await queue.EnterSemaphoreMutationAsync() : null;
		if (canceled) await Assert.ThrowsAsync<OperationCanceledException>(async () => await queue.NotifyCounted(target, 1));
		else await Assert.ThrowsAsync<IOException>(async () => await queue.NotifyCounted(target, 1));
		await Assert.That(stored).IsNull();
		await Assert.That(jobs.Count).IsEqualTo(1);
		await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		await Assert.That(ran).IsEqualTo(0);
		fail = false;
		var firstRetry = queue.NotifyCounted(target, 1).AsTask();
		await retryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var secondRetry = queue.NotifyCounted(target, 1).AsTask();
		try
		{
			await Assert.That(secondRetry.IsCompleted).IsFalse();
			await Assert.That(cleanupAttempts).IsEqualTo(1);
			await Assert.That(ran).IsEqualTo(0);
			await Assert.That(queue.GetQueueUsage().Total).IsEqualTo(1);
		}
		finally { finishRetry.TrySetResult(); }
		await Task.WhenAll(firstRetry, secondRetry);
		await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Assert.That(jobs.Count).IsEqualTo(0);
		await Assert.That(ran).IsEqualTo(1);
	}

}
