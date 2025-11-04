using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributeFlagsQuery : IStreamQuery<SharpAttributeFlag>, ICacheable
{
	public string CacheKey => "global:AttributeFlagsList";
	
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}