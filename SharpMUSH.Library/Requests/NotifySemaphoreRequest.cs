using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Requests;

public record NotifySemaphoreRequest(DbRefAttribute DbRefAttribute, int OldValue) : IRequest;

public record NotifyAllSemaphoreRequest(DbRefAttribute DbRefAttribute) : IRequest;