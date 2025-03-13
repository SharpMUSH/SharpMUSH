using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record LinkExitCommand(SharpExit Exit, AnySharpContainer Location) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [Exit.Object.DBRef.ToString(), Location.Object().DBRef.ToString()];
	public string[] CacheTags => [];
}