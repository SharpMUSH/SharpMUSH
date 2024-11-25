using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetObjectFlagQuery(string FlagName) : IQuery<SharpObjectFlag?>, ICacheable;

public record GetObjectFlagsQuery(string Id) : IQuery<IEnumerable<SharpObjectFlag>?>, ICacheable;

public record GetAllObjectFlagsQuery() : IQuery<IEnumerable<SharpObjectFlag>>, ICacheable;