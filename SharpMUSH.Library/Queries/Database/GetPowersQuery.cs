using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetPowersQuery : IStreamQuery<SharpPower>, ICacheable
{
	public string CacheKey => "global:ObjectPowersList";
	public string[] CacheTags => [Definitions.CacheTags.PowerList];
}

/// <summary>
/// The powers set on one object, keyed by its provider id. The read behind <c>SharpObject.Powers</c>,
/// so <c>HasPower</c> / <c>IsGuest</c> / <c>IsSee_All</c> answer from cache. Invalidated by key from
/// <c>SetObjectPowerCommand</c> / <c>UnsetObjectPowerCommand</c> and by the
/// <see cref="Definitions.CacheTags.ObjectPowers"/> tag from <c>DeleteObjectCommand</c>, because
/// dbref numbers are reused.
/// </summary>
public record GetObjectPowersQuery(string Id) : IStreamQuery<SharpPower>, ICacheable
{
	public string CacheKey => Definitions.CacheKeys.ObjectPowers(Id);
	public string[] CacheTags => [Definitions.CacheTags.ObjectPowers];
}
