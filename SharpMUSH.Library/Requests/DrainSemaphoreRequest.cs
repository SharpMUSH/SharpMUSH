using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Requests;

public record DrainSemaphoreRequest(DbRefAttribute DbRefAttribute) : IRequest;