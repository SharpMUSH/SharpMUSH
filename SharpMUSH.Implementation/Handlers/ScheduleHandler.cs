using Mediator;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

public class ScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<QueueCommandListRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(QueueCommandListRequest request, CancellationToken cancellationToken)
	{
		return await scheduler.WriteCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue);
	}
}

public class AsyncScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<QueueAttributeRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(QueueAttributeRequest request, CancellationToken cancellationToken)
	{
		return await scheduler.WriteAsyncAttribute(request.Input, request.DbRefAttribute, request.Executor);
	}
}

public class GetScheduledTasksHandler(ITaskScheduler scheduler)
	: IStreamQueryHandler<ScheduleSemaphoreQuery, SemaphoreTaskData>
{
	public IAsyncEnumerable<SemaphoreTaskData> Handle(ScheduleSemaphoreQuery query,
		CancellationToken cancellationToken)
		=> query.Query.Match(scheduler.GetSemaphoreTasks, scheduler.GetSemaphoreTasks, scheduler.GetSemaphoreTasks);
}

public class GetDelayTasksHandler(ITaskScheduler scheduler)
	: IStreamQueryHandler<ScheduleDelayQuery, long>
{
	public IAsyncEnumerable<long> Handle(ScheduleDelayQuery query,
		CancellationToken cancellationToken)
		=> scheduler.GetDelayTasks(query.Query);
}

public class DelayedScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<QueueDelayedCommandListRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(QueueDelayedCommandListRequest request, CancellationToken cancellationToken)
	{
		return await scheduler.WriteCommandList(request.Command, request.State, request.Delay);
	}
}

public class ScheduleTimeoutHandler(ITaskScheduler scheduler) : IRequestHandler<QueueCommandListWithTimeoutRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(QueueCommandListWithTimeoutRequest request, CancellationToken cancellationToken)
	{
		return await scheduler.WriteCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue,
			request.Timeout);
	}
}

public class ScheduleNotifyHandler(ITaskScheduler scheduler) : IRequestHandler<NotifySemaphoreRequest, IReadOnlyList<QueueAdmissionResult>>
{
	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Handle(NotifySemaphoreRequest request, CancellationToken cancellationToken)
	{
		return await scheduler.Notify(request.DbRefAttribute, request.OldValue, request.Count);
	}
}

public class RescheduleSemaphoreHandler(ITaskScheduler scheduler) : IRequestHandler<RescheduleSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(RescheduleSemaphoreRequest request, CancellationToken cancellationToken)
	{
		await scheduler.RescheduleSemaphoreTask(request.ProcessIdentifier, request.NewDelay);
		return await Unit.ValueTask;
	}
}

public class ScheduleNotifyAllHandler(ITaskScheduler scheduler) : IRequestHandler<NotifyAllSemaphoreRequest, IReadOnlyList<QueueAdmissionResult>>
{
	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Handle(NotifyAllSemaphoreRequest request, CancellationToken cancellationToken)
	{
		return await scheduler.NotifyAll(request.DbRefAttribute);
	}
}

public class ScheduleDrainHandler(ITaskScheduler scheduler) : IRequestHandler<DrainSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(DrainSemaphoreRequest request, CancellationToken cancellationToken)
	{
		await scheduler.Drain(request.DbRefAttribute, request.Count);
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

public class GetEnqueueTasksHandler(ITaskScheduler scheduler)
	: IStreamQueryHandler<ScheduleEnqueueQuery, long>
{
	public IAsyncEnumerable<long> Handle(ScheduleEnqueueQuery query,
		CancellationToken cancellationToken)
		=> scheduler.GetEnqueueTasks(query.Query);
}

public class GetAllTasksHandler(ITaskScheduler scheduler)
	: IStreamQueryHandler<ScheduleAllTasksQuery, (string Group, (DateTimeOffset, OneOf<string, DBRef>)[])>
{
	public IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf<string, DBRef>)[])> Handle(ScheduleAllTasksQuery query,
		CancellationToken cancellationToken)
		=> scheduler.GetAllTasks();
}

public class HaltByPidHandler(ITaskScheduler scheduler) : IRequestHandler<HaltByPidRequest, bool>
{
	public async ValueTask<bool> Handle(HaltByPidRequest request, CancellationToken cancellationToken)
		=> await scheduler.HaltByPid(request.Pid);
}

public class ModifyQRegistersHandler(ITaskScheduler scheduler) : IRequestHandler<ModifyQRegistersRequest, bool>
{
	public async ValueTask<bool> Handle(ModifyQRegistersRequest request, CancellationToken cancellationToken)
		=> await scheduler.ModifyQRegisters(request.DbRefAttribute, request.QRegisters);
}