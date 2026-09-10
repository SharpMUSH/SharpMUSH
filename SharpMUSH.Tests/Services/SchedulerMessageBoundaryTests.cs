using Mediator;
using NSubstitute;
using SharpMUSH.Implementation.Handlers;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class SchedulerMessageBoundaryTests
{
	[Test]
	[Arguments("command")]
	[Arguments("attribute")]
	[Arguments("delay")]
	[Arguments("timeout")]
	[Arguments("notify")]
	[Arguments("notifyAll")]
	[Arguments("drain")]
	[Arguments("drainCounted")]
	public async Task CancelledMessageDoesNotMutateTheScheduler(string operation)
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		var target = new DbRefAttribute(new DBRef(1), ["SEMAPHORE"]);
		var token = new CancellationToken(true);
		var cancelled = false;
		try
		{
			switch (operation)
			{
				case "command": await new AdmissionScheduleHandler(scheduler).Handle(new AdmitCommandListRequest(MarkupText.Plain("think test"), ParserState.Empty, target, 0), token); break;
				case "attribute": await new AdmissionAsyncScheduleHandler(scheduler).Handle(new AdmitAttributeRequest(() => ValueTask.FromResult(ParserState.Empty), target), token); break;
				case "delay": await new AdmissionDelayedScheduleHandler(scheduler).Handle(new AdmitDelayedCommandListRequest(MarkupText.Plain("think test"), ParserState.Empty, TimeSpan.Zero), token); break;
				case "timeout": await new AdmissionScheduleTimeoutHandler(scheduler).Handle(new AdmitCommandListWithTimeoutRequest(MarkupText.Plain("think test"), ParserState.Empty, target, 0, TimeSpan.Zero), token); break;
				case "notify": await new AdmissionScheduleNotifyHandler(scheduler).Handle(new NotifySemaphoreCountedRequest(target, 0, 1), token); break;
				case "notifyAll": await new AdmissionScheduleNotifyAllHandler(scheduler).Handle(new NotifyAllSemaphoreCountedRequest(target), token); break;
				case "drain": await new ScheduleDrainHandler(scheduler).Handle(new DrainSemaphoreRequest(target), token); break;
				case "drainCounted": await new ScheduleDrainCountedHandler(scheduler).Handle(new DrainSemaphoreCountedRequest(target), token); break;
			}
		}
		catch (OperationCanceledException ex) when (ex.CancellationToken == token) { cancelled = true; }
		await Assert.That(cancelled).IsTrue();
		await Assert.That(scheduler.ReceivedCalls().Any()).IsFalse();
	}

	[Test]
	public async Task PublishedDrainRequestAndHandlerRemainCallable()
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		var target = new DbRefAttribute(new DBRef(1), ["SEMAPHORE"]);
		var request = new DrainSemaphoreRequest(target, 2);
		var (actualTarget, count) = request;
		IRequestHandler<DrainSemaphoreRequest> handler = new ScheduleDrainHandler(scheduler);
		ValueTask<Unit> result = handler.Handle(request, CancellationToken.None);
		await result;
		await Assert.That(actualTarget).IsEqualTo(target);
		await Assert.That(count).IsEqualTo(2);
		var call = scheduler.ReceivedCalls().Single();
		await Assert.That(call.GetMethodInfo().Name).IsEqualTo(nameof(ITaskScheduler.Drain));
		await Assert.That(call.GetArguments()[0]!.ToString()).IsEqualTo(target.ToString());
		await Assert.That((int?)call.GetArguments()[1]).IsEqualTo(2);
	}
}
