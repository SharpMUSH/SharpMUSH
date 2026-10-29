using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal.Navigation;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>The game's portal navigation layout. Read on every portal visit, so cached until a save or a reset.</summary>
public record GetNavLayoutQuery : IQuery<Found<NavLayout>>, ICacheable
{
	public const string Key = "portal-nav-layout";

	public string CacheKey => Key;
	public string[] CacheTags => [];
}
