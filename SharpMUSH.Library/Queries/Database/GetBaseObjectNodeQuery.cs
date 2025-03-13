using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetBaseObjectNodeQuery(DBRef DBRef) : IQuery<SharpObject?>/*, ICacheable*/
{
	// public string CacheKey => $"object:{DBRef}";
	// public string[] CacheTags => [];
}