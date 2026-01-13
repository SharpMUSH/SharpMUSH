using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Requests;

public record ModifyQRegistersRequest(DbRefAttribute DbRefAttribute, System.Collections.Generic.Dictionary<string, MString> QRegisters) : IRequest<bool>;
