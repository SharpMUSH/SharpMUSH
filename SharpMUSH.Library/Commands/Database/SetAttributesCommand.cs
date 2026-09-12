using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Sets many attributes on one object in one store write, flags and all.
/// </summary>
/// <remarks>
/// Each write lands as <see cref="SetAttributeCommand"/> would land it, and its leaf then gains the
/// write's flags. Nothing is permission-checked: this is for loading a database's state as it stands,
/// which is what the PennMUSH importer does. The keys are the union of those each write would clear
/// alone, so the batch is invalidated once rather than once per attribute.
/// </remarks>
public record SetAttributesCommand(DBRef DBRef, IReadOnlyList<AttributeWrite> Attributes) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys =>
	[
		.. Attributes
			.SelectMany(write => Definitions.CacheKeys.AttributesTouchedBy(DBRef, write.Path))
			.Distinct(StringComparer.Ordinal)
	];

	public string[] CacheTags =>
	[
		Definitions.CacheKeys.AttributesTag(DBRef.Number),
		Definitions.CacheTags.InheritedAttributes
	];
}
