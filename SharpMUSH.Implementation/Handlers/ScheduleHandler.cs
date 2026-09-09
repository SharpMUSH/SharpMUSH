using System.Runtime.CompilerServices;
using Mediator;
using SharpMUSH.Library.ParserInterfaces;
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
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.AdmitCommandList(request.Command, request.State, request.DbRefAttribute, request.OldValue, request.ManageSemaphoreCount);
	}
}

public class AdmissionAsyncScheduleHandler(ITaskScheduler scheduler) : IRequestHandler<AdmitAttributeRequest, QueueAdmissionResult>
{
	public async ValueTask<QueueAdmissionResult> Handle(AdmitAttributeRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.AdmitAsyncAttribute(request.Input, request.DbRefAttribute, request.Executor);
	}
}

public class GetScheduledTasksHandler(ITaskScheduler scheduler)
	: IStreamQueryHandler<ScheduleSemaphoreQuery, SemaphoreTaskData>
{
	public async IAsyncEnumerable<SemaphoreTaskData> Handle(ScheduleSemaphoreQuery query,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		var token = ExecutionBudget.CurrentToken;
		await foreach (var row in query.Query.Match(scheduler.GetSemaphoreTasks, scheduler.GetSemaphoreTasks, scheduler.GetSemaphoreTasks).WithCancellation(token)) yield return row;
	}
}

public class GetDelayTasksHandler(ITaskScheduler scheduler)
	: IStreamQueryHandler<ScheduleDelayQuery, long>
{
	public async IAsyncEnumerable<long> Handle(ScheduleDelayQuery query,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		var token = ExecutionBudget.CurrentToken;
		await foreach (var row in scheduler.GetDelayTasks(query.Query).WithCancellation(token)) yield return row;
	}
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

public class AdmissionScheduleNotifyHandler(ITaskScheduler scheduler) : IRequestHandler<NotifySemaphoreCountedRequest, IReadOnlyList<QueueAdmissionResult>>
{
	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Handle(NotifySemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.NotifyCounted(request.DbRefAttribute, request.OldValue, request.Count);
	}
}

public class RescheduleSemaphoreHandler(ITaskScheduler scheduler) : IRequestHandler<RescheduleSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(RescheduleSemaphoreRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		await scheduler.RescheduleSemaphoreTask(request.ProcessIdentifier, request.NewDelay);
		return await Unit.ValueTask;
	}
}

public class AdmissionScheduleNotifyAllHandler(ITaskScheduler scheduler) : IRequestHandler<NotifyAllSemaphoreCountedRequest, IReadOnlyList<QueueAdmissionResult>>
{
	public async ValueTask<IReadOnlyList<QueueAdmissionResult>> Handle(NotifyAllSemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.NotifyAllCounted(request.DbRefAttribute);
	}
}

public class ScheduleDrainHandler(ITaskScheduler scheduler) : IRequestHandler<DrainSemaphoreRequest>
{
	public async ValueTask<Unit> Handle(DrainSemaphoreRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		await scheduler.Drain(request.DbRefAttribute, request.Count);
		return await Unit.ValueTask;
	}
}

public class ScheduleDrainCountedHandler(ITaskScheduler scheduler) : IRequestHandler<DrainSemaphoreCountedRequest, int>
{
	public async ValueTask<int> Handle(DrainSemaphoreCountedRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.DrainCounted(request.DbRefAttribute, request.Count);
	}
}

public class ScheduleHaltHandler(ITaskScheduler scheduler) : IRequestHandler<HaltObjectQueueRequest>
{
	public async ValueTask<Unit> Handle(HaltObjectQueueRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		await scheduler.Halt(request.DbRef);
		return await Unit.ValueTask;
	}
}

public class GetEnqueueTasksHandler(ITaskScheduler scheduler)
	: IStreamQueryHandler<ScheduleEnqueueQuery, long>
{
	public async IAsyncEnumerable<long> Handle(ScheduleEnqueueQuery query,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		var token = ExecutionBudget.CurrentToken;
		await foreach (var row in scheduler.GetEnqueueTasks(query.Query).WithCancellation(token)) yield return row;
	}
}

public class GetAllTasksHandler(ITaskScheduler scheduler)
	: IStreamQueryHandler<ScheduleAllTasksQuery, (string Group, (DateTimeOffset, OneOf<string, DBRef>)[])>
{
	public async IAsyncEnumerable<(string Group, (DateTimeOffset, OneOf<string, DBRef>)[])> Handle(ScheduleAllTasksQuery query,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		var token = ExecutionBudget.CurrentToken;
		await foreach (var row in scheduler.GetAllTasks().WithCancellation(token)) yield return row;
	}
}

public class HaltByPidHandler(ITaskScheduler scheduler) : IRequestHandler<HaltByPidRequest, bool>
{
	public async ValueTask<bool> Handle(HaltByPidRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.HaltByPid(request.Pid);
	}
}

public class ModifyQRegistersHandler(ITaskScheduler scheduler) : IRequestHandler<ModifyQRegistersRequest, bool>
{
	public async ValueTask<bool> Handle(ModifyQRegistersRequest request, CancellationToken cancellationToken)
	{
		using var scope = ExecutionBudget.EnterLinked(cancellationToken);
		return await scheduler.ModifyQRegisters(request.DbRefAttribute, request.QRegisters);
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
