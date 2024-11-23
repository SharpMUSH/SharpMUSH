using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

[CacheableQuery]
public record GetAttributeQuery(DBRef DBRef, string[] Attribute) : IRequest<IEnumerable<SharpAttribute>?>;