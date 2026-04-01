using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record WipeAttributeCommand(DBRef DBRef, string[] Attribute) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => Attribute.Select(attr => $"attribute:{DBRef}:{attr})").Append($"commands:{DBRef}").ToArray();
	public string[] CacheTags => [Definitions.CacheTags.ObjectAttributes];
}
