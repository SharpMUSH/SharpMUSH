using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributesQuery(DBRef DBRef, string Pattern) : IQuery<IEnumerable<SharpAttribute>?>/*, ICacheable*/;