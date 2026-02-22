using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Requests;

public record HaltObjectQueueRequest(DBRef DbRef) : IRequest;