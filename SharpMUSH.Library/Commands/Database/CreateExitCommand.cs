using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateExitCommand(string Name, string[] Aliases, AnySharpContainer Location, SharpPlayer Creator)
	: ICommand<DBRef>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Contents(Location.Object().DBRef), Definitions.CacheKeys.Object(Creator.Object.DBRef)];

	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheTags.ExitList,
		Definitions.CacheTags.ObjectList
	];
}