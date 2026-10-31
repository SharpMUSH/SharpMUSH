using SharpMUSH.Library.Models.SchedulerModels;
using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Requests;

public record AdmitCommandListRequest(
	MString Command,
	ParserState State,
	DbRefAttribute DbRefAttribute,
	int OldValue,
	bool ManageSemaphoreCount = false) : IRequest<QueueAdmissionResult>;

public record AdmitAttributeRequest(
	Func<ValueTask<ParserState>> Input,
	DbRefAttribute DbRefAttribute,
	DBRef? Executor = null,
	DBRef? Enactor = null) : IRequest<QueueAdmissionResult>;

/// <summary>An attribute queued once <paramref name="Work"/>, run off the queue, has built its state; see <c>ITaskScheduler.AdmitAttributeAfter</c>.</summary>
public record AdmitAttributeAfterRequest(
	Func<ValueTask<ParserState?>> Work,
	DbRefAttribute DbRefAttribute,
	DBRef? Executor = null,
	DBRef? Enactor = null) : IRequest<QueueAdmissionResult>;

/// <summary>Work run off the queue whose result runs as a queue entry; see <c>ITaskScheduler.AdmitWorkAfter</c>.</summary>
public record AdmitWorkAfterRequest(
	Func<ValueTask<Func<ValueTask<CallState?>>?>> Work,
	string TriggerName,
	DBRef Executor,
	DBRef? Enactor = null) : IRequest<QueueAdmissionResult>;

public record AdmitDelayedCommandListRequest(
	MString Command,
	ParserState State,
	TimeSpan Delay) : IRequest<QueueAdmissionResult>;

public record AdmitCommandListWithTimeoutRequest(
	MString Command,
	ParserState State,
	DbRefAttribute DbRefAttribute,
	int OldValue,
	TimeSpan Timeout,
	bool ManageSemaphoreCount = false) : IRequest<QueueAdmissionResult>;

public record ReserveCommandListRequest(MString Command, ParserState State) : IRequest<QueueCommandReservation>;
