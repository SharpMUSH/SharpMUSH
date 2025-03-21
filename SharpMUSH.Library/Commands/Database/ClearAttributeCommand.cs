using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record ClearAttributeCommand(DBRef DBRef, string[] Attribute) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [$"attribute:{DBRef}:{string.Join("`", Attribute)})"];
	public string[] CacheTags => [];
}