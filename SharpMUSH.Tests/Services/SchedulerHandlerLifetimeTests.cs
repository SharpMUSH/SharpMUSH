using System.Runtime.CompilerServices;
using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Implementation.Handlers;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.Services;

public class SchedulerHandlerLifetimeTests
{
	[Test]
	[Arguments("command", false)]
	[Arguments("attribute", false)]
	[Arguments("delay", false)]
	[Arguments("timeout", false)]
	[Arguments("legacy-command", false)]
	[Arguments("legacy-attribute", false)]
	[Arguments("legacy-delay", false)]
	[Arguments("legacy-timeout", false)]
	[Arguments("reserve", false)]
	[Arguments("command", true)]
	public async Task LiveAdmissionCancellationReachesTheBlockedProvider(string operation, bool inherited)
	{
		var mediator = Substitute.For<IMediator>();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		TimeSpan? observedRemaining = null;
		async ValueTask<AnyOptionalSharpObject> Lookup(CancellationToken token)
		{
			observedRemaining = ExecutionBudget.Current?.Remaining;
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			throw new InvalidOperationException("The blocked provider must settle cancellation.");
		}
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>()).Returns(call => Lookup(call.Arg<CancellationToken>()));
		var factory = Substitute.For<ISchedulerFactory>();
		factory.GetScheduler().Returns(Substitute.For<IScheduler>());
		await using var scheduler = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(), factory,
			Substitute.For<IAttributeService>(), mediator, NullLogger<Scheduler>.Instance);
		var target = new DbRefAttribute(new DBRef(10), ["SEMAPHORE"]);
		var state = ParserState.Empty with { Executor = new DBRef(10) };
		var command = MarkupText.Plain("think never admitted");
		using var request = new CancellationTokenSource();
		using var parent = inherited ? new ExecutionBudget(TimeSpan.FromSeconds(30)) : null;
		using var parentScope = parent?.Enter();
		async Task Invoke()
		{
			switch (operation)
			{
				case "command": await new AdmissionScheduleHandler(scheduler).Handle(new(command, state, target, 0), request.Token); break;
				case "attribute": await new AdmissionAsyncScheduleHandler(scheduler).Handle(new(() => ValueTask.FromResult(state), target), request.Token); break;
				case "delay": await new AdmissionDelayedScheduleHandler(scheduler).Handle(new(command, state, TimeSpan.FromHours(1)), request.Token); break;
				case "timeout": await new AdmissionScheduleTimeoutHandler(scheduler).Handle(new(command, state, target, 0, TimeSpan.FromHours(1)), request.Token); break;
				case "legacy-command": await new ScheduleHandler(scheduler).Handle(new(command, state, target, 0), request.Token); break;
				case "legacy-attribute": await new AsyncScheduleHandler(scheduler).Handle(new(() => ValueTask.FromResult(state), target), request.Token); break;
				case "legacy-delay": await new DelayedScheduleHandler(scheduler).Handle(new(command, state, TimeSpan.FromHours(1)), request.Token); break;
				case "legacy-timeout": await new ScheduleTimeoutHandler(scheduler).Handle(new(command, state, target, 0, TimeSpan.FromHours(1)), request.Token); break;
				case "reserve": using (await new ReservedCommandListHandler(scheduler).Handle(new(command, state), request.Token)) { } break;
			}
		}
		var invocation = Invoke();
		try
		{
			var observedToken = await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
			await Assert.That(observedToken.CanBeCanceled).IsTrue();
			if (inherited) await Assert.That(observedRemaining <= TimeSpan.FromSeconds(30)).IsTrue();
			request.Cancel();
			await Assert.That(observedToken.IsCancellationRequested).IsTrue();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
			await Assert.That(scheduler.GetQueueUsage().Total).IsEqualTo(0);
		}
		finally
		{
			cleanup.Cancel();
			try { await invocation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task StreamHandlerCarriesCancellationAfterTheFirstYield(bool enumerationToken)
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var cleanup = new CancellationTokenSource();
		async IAsyncEnumerable<long> Rows([EnumeratorCancellation] CancellationToken token = default)
		{
			yield return 1;
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
		}
		scheduler.GetDelayTasks(Arg.Any<DBRef>()).Returns(_ => Rows());
		using var request = new CancellationTokenSource();
		var handler = new GetDelayTasksHandler(scheduler);
		var rows = handler.Handle(new ScheduleDelayQuery(new DBRef(10)), enumerationToken ? CancellationToken.None : request.Token);
		async Task Read()
		{
			await foreach (var _ in rows.WithCancellation(enumerationToken ? request.Token : CancellationToken.None)) { }
		}
		var invocation = Read();
		try
		{
			var observed = await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
			request.Cancel();
			await Assert.That(observed.IsCancellationRequested).IsTrue();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
		}
		finally
		{
			cleanup.Cancel();
			try { await invocation; } catch (OperationCanceledException) { }
		}
	}
}
