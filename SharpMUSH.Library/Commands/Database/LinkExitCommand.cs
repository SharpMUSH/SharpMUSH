using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record LinkExitCommand(SharpExit Exit, AnySharpContainer Location) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [$"object:{Exit.Object.DBRef}"];
	public string[] CacheTags => [];
}