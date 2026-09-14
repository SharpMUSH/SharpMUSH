using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>Changes an existing leaf attribute owner without changing its ancestors, value or flags.</summary>
public record SetAttributeOwnerCommand(DBRef DBRef, string[] Attribute, SharpPlayer Owner) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => Definitions.CacheKeys.AttributesTouchedBy(DBRef, Attribute);
	public string[] CacheTags => [Definitions.CacheKeys.AttributesTag(DBRef.Number), Definitions.CacheTags.InheritedAttributes];
}
