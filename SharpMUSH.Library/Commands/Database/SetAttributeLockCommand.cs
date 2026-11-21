using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Sets one key of an attribute on <paramref name="DBRef"/>, or takes it off with a null <paramref name="Key"/>; see
/// <see cref="IAttributeLockStore.SetAttributeLockAsync"/>. Drops the attribute caches a flag change drops.
/// </summary>
public record SetAttributeLockCommand(DBRef DBRef, SharpAttribute Target, AttributeLockKind Kind, string? Key)
	: ICommand, ICacheInvalidating
{
	public string[] CacheKeys => Definitions.CacheKeys.AttributesTouchedBy(DBRef, Target.LongName.Split('`'));

	public string[] CacheTags =>
	[
		Definitions.CacheKeys.AttributesTag(DBRef.Number),
		Definitions.CacheTags.InheritedAttributes
	];
}

/// <summary>
/// Sets one key of the attribute table's entry <paramref name="Name"/>, or takes it off; see
/// <see cref="IAttributeLockStore.SetAttributeEntryLockAsync"/>. The entry caches are dropped.
/// </summary>
public record SetAttributeEntryLockCommand(string Name, AttributeLockKind Kind, string? Key)
	: ICommand<DiscriminatedUnions.Found<SharpAttributeEntry>>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList, Definitions.CacheTags.AttributeEntry];
}
