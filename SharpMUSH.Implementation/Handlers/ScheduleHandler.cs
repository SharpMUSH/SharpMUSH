using Mediator;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.DiscriminatedUnions;
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
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.AdmitCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue, request.ManageSemaphoreCount);
	}
}

public class AdmissionAsyncScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<AdmitAttributeRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(AdmitAttributeRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.AdmitAsyncAttribute(request.Input, request.DbRefAttribute, request.Executor, request.Enactor);
	}
}

public class GetScheduledTasksHandler(ITaskQueueReader queue)
	: IStreamQueryHandler<ScheduleSemaphoreQuery, SemaphoreTaskData>
{
	public IAsyncEnumerable<SemaphoreTaskData> Handle(ScheduleSemaphoreQuery query,
		CancellationToken cancellationToken)
		=> SchedulerQueryLifetime.Read(() => query.Query switch
		{
			long pid => queue.GetSemaphoreTasks(pid),
			DBRef obj => queue.GetSemaphoreTasks(obj),
			DbRefAttribute attribute => queue.GetSemaphoreTasks(attribute)
		}, cancellationToken);
}

public class GetDelayTasksHandler(ITaskQueueReader queue)
	: IStreamQueryHandler<ScheduleDelayQuery, long>
{
	public IAsyncEnumerable<long> Handle(ScheduleDelayQuery query,
		CancellationToken cancellationToken)
		=> SchedulerQueryLifetime.Read(() => queue.GetDelayTasks(query.Query), cancellationToken);
}

public class AdmissionDelayedScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<AdmitDelayedCommandListRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(AdmitDelayedCommandListRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.AdmitCommandList(request.Command, request.State, request.Delay);
	}
}

public class AdmissionScheduleTimeoutHandler(ITaskScheduler scheduler) : IRequestHandler<AdmitCommandListWithTimeoutRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(AdmitCommandListWithTimeoutRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.AdmitCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue,
			request.Timeout, request.ManageSemaphoreCount);
	}
}

public class AdmissionScheduleNotifyHandler(ISemaphoreQueue semaphores) : IRequestHandler<NotifySemaphoreCountedRequest, IReadOnlyList<QueueAdmissionResult>>
{
	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Handle(NotifySemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await semaphores.NotifyCounted(request.DbRefAttribute, request.OldValue, request.Count);
	}
}

public class RescheduleSemaphoreHandler(ISemaphoreQueue semaphores) : IRequestHandler<RescheduleSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(RescheduleSemaphoreRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		await semaphores.RescheduleSemaphoreTask(request.ProcessIdentifier, request.NewDelay);
		return await Unit.ValueTask;
	}
}

public class AdmissionScheduleNotifyAllHandler(ISemaphoreQueue semaphores) : IRequestHandler<NotifyAllSemaphoreCountedRequest, IReadOnlyList<QueueAdmissionResult>>
{
	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Handle(NotifyAllSemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await semaphores.NotifyAllCounted(request.DbRefAttribute);
	}
}

public class ScheduleDrainHandler(ISemaphoreQueue semaphores) : IRequestHandler<DrainSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(DrainSemaphoreRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		await semaphores.Drain(request.DbRefAttribute, request.Count);
		return await Unit.ValueTask;
	}
}

public class ScheduleDrainCountedHandler(ISemaphoreQueue semaphores) : IRequestHandler<DrainSemaphoreCountedRequest, int>
{
	public async ValueTask<int> Handle(DrainSemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await semaphores.DrainCounted(request.DbRefAttribute, request.Count);
	}
}

public class ScheduleHaltHandler(ITaskQueueControl control) : IRequestHandler<HaltObjectQueueRequest>
{
	public async ValueTask<Unit> Handle(HaltObjectQueueRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		await control.Halt(request.DbRef);
		return await Unit.ValueTask;
	}
}

public class GetEnqueueTasksHandler(ITaskQueueReader queue)
	: IStreamQueryHandler<ScheduleEnqueueQuery, long>
{
	public IAsyncEnumerable<long> Handle(ScheduleEnqueueQuery query,
		CancellationToken cancellationToken)
		=> SchedulerQueryLifetime.Read(() => queue.GetEnqueueTasks(query.Query), cancellationToken);
}

public class GetAllTasksHandler(ITaskQueueReader queue)
	: IStreamQueryHandler<ScheduleAllTasksQuery, (string Group, (DateTimeOffset, NameOrDbRef)[])>
{
	public IAsyncEnumerable<(string Group, (DateTimeOffset, NameOrDbRef)[])> Handle(ScheduleAllTasksQuery query,
		CancellationToken cancellationToken)
		=> SchedulerQueryLifetime.Read(() => queue.GetAllTasks(), cancellationToken);
}

public class HaltByPidHandler(ITaskQueueControl control) : IRequestHandler<HaltByPidRequest, bool>
{
	public async ValueTask<bool> Handle(HaltByPidRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await control.HaltByPid(request.Pid);
	}
}

public class ModifyQRegistersHandler(ISemaphoreQueue semaphores) : IRequestHandler<ModifyQRegistersRequest, bool>
{
	public async ValueTask<bool> Handle(ModifyQRegistersRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await semaphores.ModifyQRegisters(request.DbRefAttribute, request.QRegisters);
	}
}

public class ReservedCommandListHandler(ITaskScheduler scheduler) : IRequestHandler<ReserveCommandListRequest, QueueCommandReservation>
{
	public async ValueTask<QueueCommandReservation> Handle(ReserveCommandListRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.ReserveCommandList(request.Command, request.State);
	}
}
