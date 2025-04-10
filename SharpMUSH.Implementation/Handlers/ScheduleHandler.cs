using Mediator;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Handlers;

public class ScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<QueueCommandListRequest>
{
	public async ValueTask<Unit> Handle(QueueCommandListRequest request, CancellationToken cancellationToken)
	{
		await scheduler.WriteCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue);
		return await Unit.ValueTask;
	}
}


public class DelayedScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<QueueDelayedCommandListRequest>
{
	public async ValueTask<Unit> Handle(QueueDelayedCommandListRequest request, CancellationToken cancellationToken)
	{
		await scheduler.WriteCommandList(request.Command, request.State, request.Delay);
		return await Unit.ValueTask;
	}
}

public class ScheduleTimeoutHandler(ITaskScheduler scheduler) : IRequestHandler<QueueCommandListWithTimeoutRequest>
{
	public async ValueTask<Unit> Handle(QueueCommandListWithTimeoutRequest request, CancellationToken cancellationToken)
	{
		await scheduler.WriteCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue, request.Timeout);
		return await Unit.ValueTask;
	}
}

public class ScheduleNotifyHandler(ITaskScheduler scheduler) : IRequestHandler<NotifySemaphoreRequest>
{
	public async ValueTask<Unit> Handle(NotifySemaphoreRequest request, CancellationToken cancellationToken)
	{
		await scheduler.Notify(request.DbRefAttribute, request.OldValue);
		return await Unit.ValueTask;
	}
}

public class ScheduleNotifyAllHandler(ITaskScheduler scheduler) : IRequestHandler<NotifyAllSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(NotifyAllSemaphoreRequest request, CancellationToken cancellationToken)
	{
		await scheduler.NotifyAll(request.DbRefAttribute);
		return await Unit.ValueTask;
	}
}

public class ScheduleDrainHandler(ITaskScheduler scheduler) : IRequestHandler<DrainSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(DrainSemaphoreRequest request, CancellationToken cancellationToken)
	{
		await scheduler.Drain(request.DbRefAttribute);
		return await Unit.ValueTask;
	}
}

public class ScheduleHaltHandler(ITaskScheduler scheduler) : IRequestHandler<HaltObjectQueueRequest>
{
	public async ValueTask<Unit> Handle(HaltObjectQueueRequest request, CancellationToken cancellationToken)
	{
		await scheduler.Halt(request.DbRef);
		return await Unit.ValueTask;
	}
}