using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

[CacheableQuery]
public record GetAttributesQuery(DBRef DBRef, string Pattern) : IRequest<IEnumerable<SharpAttribute>?>;