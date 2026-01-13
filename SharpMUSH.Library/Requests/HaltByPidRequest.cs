using Mediator;

namespace SharpMUSH.Library.Requests;

public record HaltByPidRequest(long Pid) : IRequest<bool>;
