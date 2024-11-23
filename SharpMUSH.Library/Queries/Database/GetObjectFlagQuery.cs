using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetObjectFlagQuery(string FlagName) : IQuery<SharpObjectFlag?>, ICacheable;

public record GetObjectFlagsQuery() : IQuery<IEnumerable<SharpObjectFlag>>, ICacheable;