using Mediator;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

// Keep published Unit request contracts and handler entry points for compiled callers.
public class ScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<QueueCommandListRequest>
{
	public async ValueTask<Unit> Handle(QueueCommandListRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await scheduler.WriteCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue);
		return Unit.Value;
	}
}
public class AsyncScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<QueueAttributeRequest>
{
	public async ValueTask<Unit> Handle(QueueAttributeRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await scheduler.WriteAsyncAttribute(request.Input, request.DbRefAttribute);
		return Unit.Value;
	}
}
public class DelayedScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<QueueDelayedCommandListRequest>
{
	public async ValueTask<Unit> Handle(QueueDelayedCommandListRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await scheduler.WriteCommandList(request.Command, request.State, request.Delay);
		return Unit.Value;
	}
}
public class ScheduleTimeoutHandler(ITaskScheduler scheduler) : IRequestHandler<QueueCommandListWithTimeoutRequest>
{
	public async ValueTask<Unit> Handle(QueueCommandListWithTimeoutRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await scheduler.WriteCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue, request.Timeout);
		return Unit.Value;
	}
}
public class ScheduleNotifyHandler(ITaskScheduler scheduler) : IRequestHandler<NotifySemaphoreRequest>
{
	public async ValueTask<Unit> Handle(NotifySemaphoreRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await scheduler.Notify(request.DbRefAttribute, request.OldValue, request.Count);
		return Unit.Value;
	}
}
public class ScheduleNotifyAllHandler(ITaskScheduler scheduler) : IRequestHandler<NotifyAllSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(NotifyAllSemaphoreRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await scheduler.NotifyAll(request.DbRefAttribute);
		return Unit.Value;
	}
}
