using Mediator;

namespace SharpMUSH.Library.Requests;

public record RescheduleSemaphoreRequest(long ProcessIdentifier, TimeSpan NewDelay) : IRequest;