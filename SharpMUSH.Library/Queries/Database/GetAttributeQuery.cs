using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetAttributeQuery(DBRef DBRef, string[] Attribute) : IQuery<IEnumerable<SharpAttribute>?>/*, ICacheable*/
{
	public string CacheKey => $"attribute:{DBRef}:{string.Join("`", Attribute)})";
	
	public string[] CacheTags => [Definitions.CacheTags.ObjectAttributes];
}