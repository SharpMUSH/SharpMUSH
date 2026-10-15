using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Sets an attribute entry's <c>@attribute/watch</c> pattern, or takes it off with a null <paramref name="Pattern"/>;
/// see <see cref="IAttributeStore.SetAttributeWatchAsync"/>. The entry caches, and with them the watched set every
/// attribute write reads, are dropped.
/// </summary>
public record SetAttributeWatchCommand(string Name, string? Pattern)
	: ICommand<Found<SharpAttributeEntry>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList, Definitions.CacheTags.AttributeEntry];
}
