using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Requests;

public record RescheduleSemaphoreRequest(long ProcessIdentifier, TimeSpan NewDelay) : IRequest;