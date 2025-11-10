using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetAllAttributeEntriesQuery() : IStreamQuery<SharpAttributeEntry>, ICacheable
{
	public string CacheKey => "global:AttributeEntriesList";
	
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}
