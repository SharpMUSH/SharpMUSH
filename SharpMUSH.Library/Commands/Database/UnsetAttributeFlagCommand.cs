using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Removes a flag from one attribute.
/// </summary>
/// <remarks>
/// The keys come from <see cref="CacheKeys.AttributesTouchedBy"/>, which the attribute readers build
/// their own keys from, so a reader and its invalidator cannot drift apart. Two tags: the written
/// object's own attribute entries, and the inheritance family game-wide — see
/// <see cref="CacheKeys.AttributesTag"/> for why the second cannot be scoped.
/// </remarks>
public record UnsetAttributeFlagCommand(DBRef DbRef, SharpAttribute Target, SharpAttributeFlag Flag) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => Definitions.CacheKeys.AttributesTouchedBy(DbRef, Target.LongName.Split('`'));

	public string[] CacheTags =>
	[
		Definitions.CacheKeys.AttributesTag(DbRef.Number),
		Definitions.CacheTags.InheritedAttributes
	];
}
