using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetPowersQuery : IStreamQuery<SharpPower>, ICacheable
{
	public string CacheKey => "global:ObjectPowersList";
	public string[] CacheTags => [Definitions.CacheTags.PowerList];
}
