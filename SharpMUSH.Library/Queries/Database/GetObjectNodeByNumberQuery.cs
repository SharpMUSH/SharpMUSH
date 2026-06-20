using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Loads the CURRENT object with this dbref number, cached under the number-only key
/// (<see cref="CacheKeys.Object(int)"/>). The objid (creation-time) check is intentionally NOT applied
/// here — <see cref="GetObjectNodeQuery"/> applies it on top, outside this cache, so a bare "#N" and a
/// full "#N:creation" reference share this entry and every mutation invalidates exactly it.
/// </summary>
public record GetObjectNodeByNumberQuery(int Number) : IQuery<AnyOptionalSharpObject>, ICacheable
{
	public string CacheKey => CacheKeys.Object(Number);
	public string[] CacheTags => [];
}
