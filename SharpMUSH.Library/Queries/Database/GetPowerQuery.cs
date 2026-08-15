using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetPowerQuery(string PowerName) : IQuery<SharpPower?>, ICacheable
{
	public string CacheKey => $"power-definition:{PowerName}";
	public string[] CacheTags => [Definitions.CacheTags.PowerList];
}
