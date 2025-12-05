using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetObjectNodeQuery(DBRef DBRef) : IQuery<AnyOptionalSharpObject>, ICacheable
{
	public string CacheKey => $"object:{DBRef}";
	public string[] CacheTags => [];
}