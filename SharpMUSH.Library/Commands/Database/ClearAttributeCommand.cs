using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Clears one attribute on an object, leaving its leaves in place.
/// </summary>
/// <remarks>
/// The keys come from <see cref="CacheKeys.AttributesTouchedBy"/>, which the attribute readers build
/// their own keys from, so a reader and its invalidator cannot drift apart; the tags from
/// <see cref="CacheKeys.AttributeWriteTags"/>.
/// </remarks>
public record ClearAttributeCommand(DBRef DBRef, string[] Attribute) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => Definitions.CacheKeys.AttributesTouchedBy(DBRef, Attribute);

	public string[] CacheTags => Definitions.CacheKeys.AttributeWriteTags(DBRef, Attribute);
}
