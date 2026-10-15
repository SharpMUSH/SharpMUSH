using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Puts an index on an attribute entry, building from the first object; see
/// <see cref="IAttributeIndexStore.SetAttributeIndexAsync"/>. The entry's cached copy carries the index, so the entry
/// caches are dropped.
/// </summary>
public record SetAttributeIndexCommand(string Name, AttributeIndexOptions Options)
	: ICommand<Result<SharpAttributeEntry>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList, Definitions.CacheTags.AttributeEntry];
}

/// <summary>Takes an attribute's index off, with all its entries; false when it had none.</summary>
public record DropAttributeIndexCommand(string Name) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList, Definitions.CacheTags.AttributeEntry];
}

/// <summary>
/// Indexes one batch of a building index; see <see cref="IAttributeIndexStore.BuildAttributeIndexBatchAsync"/>. A
/// batch moves the entry's resume point (and, after the last, marks it ready), so the entry caches are dropped.
/// </summary>
public record BuildAttributeIndexBatchCommand(string Name, int BatchSize)
	: ICommand<Found<AttributeIndexBuildStep>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList, Definitions.CacheTags.AttributeEntry];
}
