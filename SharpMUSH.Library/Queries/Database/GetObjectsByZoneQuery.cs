using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query to get all objects that belong to a specific zone.
/// </summary>
public record GetObjectsByZoneQuery(DbRefOrObject Zone)
	: IStreamQuery<SharpObject>, ICacheable
{
	public string CacheKey => $"zone-objects:{(Zone.Value is DBRef d ? d : ((AnySharpObject)Zone.Value!).Object().DBRef)}";
	public string[] CacheTags => [Definitions.CacheTags.ZoneObjects];
}
