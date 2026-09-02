using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries.Database;

public record GetChannelQuery(string Name) : IQuery<SharpChannel?>;

public record GetOnChannelQuery(AnySharpObject Obj) : IStreamQuery<SharpChannel>;

public record GetChannelListQuery : IStreamQuery<SharpChannel>, ICacheable
{
	public string CacheKey => "global:ChannelList";
	public string[] CacheTags => [Definitions.CacheTags.ChannelList];
}
/// <summary>The channels owned by a given object, asked directly rather than by filtering every channel.</summary>
public record GetChannelsOwnedByQuery(DBRef Owner) : IStreamQuery<SharpChannel>;
