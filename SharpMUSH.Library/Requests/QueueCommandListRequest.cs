using SharpMUSH.Library.Models.SchedulerModels;
using Mediator;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Requests;

public record QueueCommandListRequest(
	MString Command,
	ParserState State,
	DbRefAttribute DbRefAttribute,
	int OldValue,
	bool ManageSemaphoreCount = false) : IRequest<QueueAdmissionResult>;

public record QueueAttributeRequest(
	Func<ValueTask<ParserState>> Input,
	DbRefAttribute DbRefAttribute,
	DBRef? Executor = null) : IRequest<QueueAdmissionResult>;

public record QueueDelayedCommandListRequest(
	MString Command,
	ParserState State,
	TimeSpan Delay) : IRequest<QueueAdmissionResult>;

public record QueueCommandListWithTimeoutRequest(
	MString Command,
	ParserState State,
	DbRefAttribute DbRefAttribute,
	int OldValue,
	TimeSpan Timeout,
	bool ManageSemaphoreCount = false) : IRequest<QueueAdmissionResult>;

public record ReserveCommandListRequest(MString Command, ParserState State) : IRequest<QueueCommandReservation>;
