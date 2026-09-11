using System.Runtime.CompilerServices;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Implementation.Handlers;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class SchedulerQueryOriginTests
{
	private static Func<Task> ConsumeLater<T>(IAsyncEnumerable<T> rows) => async () =>
	{
		await foreach (var row in rows) { }
	};

	private static Func<Task> Capture(ITaskScheduler scheduler, string kind) => kind switch
	{
		"semaphore" => ConsumeLater(new GetScheduledTasksHandler(scheduler).Handle(new ScheduleSemaphoreQuery(new DBRef(10)), CancellationToken.None)),
		"delay" => ConsumeLater(new GetDelayTasksHandler(scheduler).Handle(new ScheduleDelayQuery(new DBRef(10)), CancellationToken.None)),
		"enqueue" => ConsumeLater(new GetEnqueueTasksHandler(scheduler).Handle(new ScheduleEnqueueQuery(new DBRef(10)), CancellationToken.None)),
		_ => ConsumeLater(new GetAllTasksHandler(scheduler).Handle(new ScheduleAllTasksQuery(), CancellationToken.None))
	};

	[Test]
	[Arguments("semaphore")]
	[Arguments("delay")]
	[Arguments("enqueue")]
	[Arguments("all")]
	public async Task DeferredQueryPreservesOriginatingCancellation(string kind)
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		using var source = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var origin = new ExecutionBudget(TimeSpan.FromSeconds(30), source.Token);
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		async IAsyncEnumerable<T> Rows<T>(T value, CancellationToken captured, [EnumeratorCancellation] CancellationToken token = default)
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(captured, token, cleanup.Token);
			entered.TrySetResult(linked.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			yield return value;
		}
		scheduler.GetSemaphoreTasks(Arg.Any<DBRef>()).Returns(_ => Rows<SemaphoreTaskData>(default!, ExecutionBudget.CurrentToken));
		scheduler.GetDelayTasks(Arg.Any<DBRef>()).Returns(_ => Rows(1L, ExecutionBudget.CurrentToken));
		scheduler.GetEnqueueTasks(Arg.Any<DBRef>()).Returns(_ => Rows(1L, ExecutionBudget.CurrentToken));
		scheduler.GetAllTasks().Returns(_ => Rows<(string, (DateTimeOffset, NameOrDbRef)[])>(("group", []), ExecutionBudget.CurrentToken));
		Func<Task> consume;
		using (origin.Enter()) consume = Capture(scheduler, kind);
		var invocation = consume();
		try
		{
			var observed = await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
			source.Cancel();
			await Assert.That(observed.IsCancellationRequested).IsTrue();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
		}
		finally
		{
			cleanup.Cancel();
			try { await invocation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	public async Task DeferredQueryCannotReplaceExpiredOriginWithFreshConsumer()
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		var calls = 0;
		scheduler.GetDelayTasks(Arg.Any<DBRef>()).Returns(_ => { calls++; return AsyncEnumerable.Empty<long>(); });
		Func<Task> consume;
		using (var origin = new ExecutionBudget(TimeSpan.Zero))
		using (origin.Enter()) consume = Capture(scheduler, "delay");
		using var consumer = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = consumer.Enter();
		await Assert.ThrowsAsync<OperationCanceledException>(consume);
		await Assert.That(calls).IsEqualTo(0);
	}

	[Test]
	public async Task StreamReentersLinkedBudgetAfterYieldWithoutLeakingToConsumer()
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		using var request = new CancellationTokenSource();
		using var cleanup = new CancellationTokenSource();
		using var consumer = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var consumerScope = consumer.Enter();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		async IAsyncEnumerable<long> Rows()
		{
			yield return 1;
			var token = ExecutionBudget.CurrentToken;
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
		}
		scheduler.GetDelayTasks(Arg.Any<DBRef>()).Returns(_ => Rows());
		await using var rows = new GetDelayTasksHandler(scheduler).Handle(new ScheduleDelayQuery(new DBRef(10)), request.Token).GetAsyncEnumerator();
		await Assert.That(await rows.MoveNextAsync()).IsTrue();
		await Assert.That(ReferenceEquals(ExecutionBudget.Current, consumer)).IsTrue();
		var next = rows.MoveNextAsync().AsTask();
		try
		{
			var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
			request.Cancel();
			await Assert.That(token.IsCancellationRequested).IsTrue();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await next.WaitAsync(TimeSpan.FromSeconds(2)));
		}
		finally
		{
			cleanup.Cancel();
			try { await next; } catch (OperationCanceledException) { }
		}
		await Assert.That(ReferenceEquals(ExecutionBudget.Current, consumer)).IsTrue();
	}
}
