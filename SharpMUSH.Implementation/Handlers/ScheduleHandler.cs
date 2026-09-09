using Mediator;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Queries;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers;

public class AdmissionScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<AdmitCommandListRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(AdmitCommandListRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await scheduler.AdmitCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue, request.ManageSemaphoreCount);
	}
}

public class AdmissionAsyncScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<AdmitAttributeRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(AdmitAttributeRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await scheduler.AdmitAsyncAttribute(request.Input, request.DbRefAttribute, request.Executor);
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

public class AdmissionDelayedScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<AdmitDelayedCommandListRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(AdmitDelayedCommandListRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await scheduler.AdmitCommandList(request.Command, request.State, request.Delay);
	}
}

public class AdmissionScheduleTimeoutHandler(ITaskScheduler scheduler) : IRequestHandler<AdmitCommandListWithTimeoutRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(AdmitCommandListWithTimeoutRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await scheduler.AdmitCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue,
			request.Timeout, request.ManageSemaphoreCount);
	}
}

public class AdmissionScheduleNotifyHandler(ITaskScheduler scheduler) : IRequestHandler<NotifySemaphoreCountedRequest, IReadOnlyList<QueueAdmissionResult>>
{
	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Handle(NotifySemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await scheduler.NotifyCounted(request.DbRefAttribute, request.OldValue, request.Count);
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

public class AdmissionScheduleNotifyAllHandler(ITaskScheduler scheduler) : IRequestHandler<NotifyAllSemaphoreCountedRequest, IReadOnlyList<QueueAdmissionResult>>
{
	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Handle(NotifyAllSemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return await scheduler.NotifyAllCounted(request.DbRefAttribute);
	}
}

public class ScheduleDrainHandler(ITaskScheduler scheduler) : IRequestHandler<DrainSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(DrainSemaphoreRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		await scheduler.Drain(request.DbRefAttribute, request.Count);
		return await Unit.ValueTask;
	}
}

public class ScheduleDrainCountedHandler(ITaskScheduler scheduler) : IRequestHandler<DrainSemaphoreCountedRequest, int>
{
	public ValueTask<int> Handle(DrainSemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return scheduler.DrainCounted(request.DbRefAttribute, request.Count);
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

public class ReservedCommandListHandler(ITaskScheduler scheduler) : IRequestHandler<ReserveCommandListRequest, QueueCommandReservation>
{
	public ValueTask<QueueCommandReservation> Handle(ReserveCommandListRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		return scheduler.ReserveCommandList(request.Command, request.State);
	}
}
