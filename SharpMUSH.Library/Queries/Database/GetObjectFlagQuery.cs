using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetObjectFlagQuery(string FlagName) : IQuery<SharpObjectFlag?>/*, ICacheable*/;

public record GetObjectFlagsQuery(string Id) : IQuery<IAsyncEnumerable<SharpObjectFlag>?>/*, ICacheable*/;

public record GetAllObjectFlagsQuery() : IQuery<IAsyncEnumerable<SharpObjectFlag>>, ICacheable
{
	public string CacheKey => "global:ObjectFlagsList";
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}