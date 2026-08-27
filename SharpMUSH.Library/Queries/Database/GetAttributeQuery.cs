using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributeQuery(DBRef DBRef, string[] Attribute) : IStreamQuery<SharpAttribute>, ICacheable
{
	public string CacheKey => $"attribute:{DBRef}:{string.Join("`", Attribute)}";

	public string[] CacheTags => [Definitions.CacheTags.ObjectAttributes];
}

public record GetLazyAttributeQuery(DBRef DBRef, string[] Attribute) : IStreamQuery<LazySharpAttribute>, ICacheable
{
	// The lazy- prefix keeps this from colliding with GetAttributeQuery, which caches a
	// different element type under the same identity. Matches the inheritance query pair.
	public string CacheKey => $"lazy-attribute:{DBRef}:{string.Join("`", Attribute)}";

	public string[] CacheTags => [Definitions.CacheTags.ObjectAttributes];
}