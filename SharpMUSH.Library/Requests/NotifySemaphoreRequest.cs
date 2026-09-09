using SharpMUSH.Library.Models.SchedulerModels;
using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Requests;

public record NotifySemaphoreRequest(DbRefAttribute DbRefAttribute, int OldValue, int Count = 1) : IRequest<IReadOnlyList<QueueAdmissionResult>>;

public record NotifyAllSemaphoreRequest(DbRefAttribute DbRefAttribute) : IRequest<IReadOnlyList<QueueAdmissionResult>>;