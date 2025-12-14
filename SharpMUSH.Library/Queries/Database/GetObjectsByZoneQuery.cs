using Mediator;
using OneOf;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query to get all objects that belong to a specific zone.
/// </summary>
public record GetObjectsByZoneQuery(OneOf<DBRef, AnySharpObject> Zone)
	: IStreamQuery<SharpObject>, ICacheable
{
	public string CacheKey => $"zone-objects:{Zone.Match(x => x, y => y.Object().DBRef)}";
	public string[] CacheTags => [Definitions.CacheTags.ZoneObjects];
}
