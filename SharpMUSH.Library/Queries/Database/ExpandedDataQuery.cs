using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record ExpandedDataQuery(SharpObject SharpObject, string TypeName) : IQuery<object?>;

public record ExpandedServerDataQuery(string TypeName) : IQuery<object?>;