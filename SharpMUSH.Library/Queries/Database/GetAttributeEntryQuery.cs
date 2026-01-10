using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributeEntryQuery(string Name) : IQuery<SharpAttributeEntry?>, ICacheable
{
	public string CacheKey => $"global:AttributeEntry:{Name}";
	
	public string[] CacheTags => [Definitions.CacheTags.AttributeEntry];
}
