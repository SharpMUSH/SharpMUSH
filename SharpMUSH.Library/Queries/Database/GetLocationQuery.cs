using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetLocationQuery(DBRef DBRef, int Depth = 1)
	: IQuery<AnyOptionalSharpContainer>, ICacheable
{
	public string CacheKey => CacheKeys.Location(DBRef.Number, Depth);
	public string[] CacheTags => [CacheKeys.LocationTag(DBRef.Number)];
}

public record GetCertainLocationQuery(string Key, string ObjectId, int Depth = 1)
	: IQuery<AnySharpContainer>, ICacheable
{
	// Key (the typed-vertex id) drives the graph traversal; ObjectId (the unified base Object().Id) is
	// the CACHE identity — typed-collection ids (node_things/N vs node_players/N) can share key values,
	// whereas the base Object().Id is exactly one per object.
	public string CacheKey => CacheKeys.LocationByKey(ObjectId, Depth);
	public string[] CacheTags => [CacheKeys.LocationTag(ObjectId)];
}
