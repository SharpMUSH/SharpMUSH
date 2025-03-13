using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetAttributeCommand(DBRef DBRef, string[] Attribute, MString Value, SharpPlayer Owner) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [$"attribute:{DBRef}:{string.Join("`", Attribute)})"];
	public string[] CacheTags => []; // [Definitions.CacheTags.ObjectAttributes];
}