using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The attribute entries <c>@attribute/watch</c> marked, with their patterns. Every attribute set, clear and wipe
/// asks, so it is cached, and every change to the table expires it (<see cref="Definitions.CacheTags.AttributeEntry"/>).
/// </summary>
public record GetWatchedAttributesQuery : IQuery<WatchedAttributes>, ICacheable
{
	public string CacheKey => "global:WatchedAttributes";

	public string[] CacheTags => [Definitions.CacheTags.AttributeEntry];

	/// <summary>Names attributes, never objects.</summary>
	public bool TagsEmbeddedObjects => false;
}
