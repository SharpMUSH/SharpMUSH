using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Clears one attribute on an object, leaving its leaves in place.
/// </summary>
/// <remarks>
/// The keys come from <see cref="CacheKeys.AttributeEditTouches"/>: <see cref="CacheKeys.AttributesTouchedBy"/>, which
/// the attribute readers build their own keys from, so a reader and its invalidator cannot drift apart, and the
/// object's node, whose modification time the write stamps. The tags come from
/// <see cref="CacheKeys.AttributeWriteTags"/>. A write that changes a watched attribute (<c>@attribute/watch</c>) publishes
/// <see cref="Notifications.AttributeChangedNotification"/>, naming <c>Enactor</c> (null: God) as its cause.
/// </remarks>
public record ClearAttributeCommand(DBRef DBRef, string[] Attribute, DBRef? Enactor = null) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => Definitions.CacheKeys.AttributeEditTouches(DBRef, Attribute);

	public string[] CacheTags => Definitions.CacheKeys.AttributeWriteTags(DBRef, Attribute);
}
