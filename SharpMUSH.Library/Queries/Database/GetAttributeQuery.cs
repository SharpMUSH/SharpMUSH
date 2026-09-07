using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// One object's attribute at a path, without inheritance. Scoped to that object by tag as well as by
/// key, because it consults nothing else — see <see cref="CacheKeys.AttributesTag"/>.
/// </summary>
public record GetAttributeQuery(DBRef DBRef, string[] Attribute) : IStreamQuery<SharpAttribute>, ICacheable
{
	public string CacheKey => CacheKeys.Attribute(DBRef, Attribute);

	public string[] CacheTags => [CacheKeys.AttributesTag(DBRef.Number), Definitions.CacheTags.AllObjectAttributes];
}

/// <inheritdoc cref="GetAttributeQuery"/>
public record GetLazyAttributeQuery(DBRef DBRef, string[] Attribute) : IStreamQuery<LazySharpAttribute>, ICacheable
{
	public string CacheKey => CacheKeys.LazyAttribute(DBRef, Attribute);

	public string[] CacheTags => [CacheKeys.AttributesTag(DBRef.Number), Definitions.CacheTags.AllObjectAttributes];
}
