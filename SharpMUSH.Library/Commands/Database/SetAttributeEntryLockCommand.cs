using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Sets one key of the attribute table's entry <paramref name="Name"/>, or takes it off with a null <paramref name="Key"/>;
/// see <see cref="IAttributeLockStore.SetAttributeEntryLockAsync"/>. The entry caches are dropped.
/// </summary>
public record SetAttributeEntryLockCommand(string Name, AttributeLockKind Kind, string? Key)
	: ICommand<DiscriminatedUnions.Found<SharpAttributeEntry>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList, Definitions.CacheTags.AttributeEntry];
}
