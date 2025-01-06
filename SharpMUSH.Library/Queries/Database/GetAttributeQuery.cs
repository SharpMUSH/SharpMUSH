using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributeQuery(DBRef DBRef, string[] Attribute) : IQuery<IEnumerable<SharpAttribute>?>/*, ICacheable*/;