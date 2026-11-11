using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// The zones <paramref name="Object"/> is in, its zone (<see cref="SharpObject.Zone"/>) first; empty when it has none.
/// See <see cref="IZoneListStore"/>.
/// </summary>
public record GetZonesOfQuery(DBRef Object) : IQuery<IReadOnlyList<AnySharpObject>>, ICacheable
{
	public string CacheKey => $"zones-of:{Object}";
	public string[] CacheTags => [CacheKeys.ObjectTag(Object.Number), Definitions.CacheTags.ZoneObjects];
}
