using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record ExpandedDataQuery(SharpObject SharpObject, string TypeName) : IQuery<string?>;

public record ExpandedServerDataQuery(string TypeName) : IQuery<string?>;