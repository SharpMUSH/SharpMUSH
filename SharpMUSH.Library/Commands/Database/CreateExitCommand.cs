using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateExitCommand(string Name, string[] Aliases, AnySharpContainer Location, SharpPlayer Creator)
	: ICommand<DBRef>, ICacheInvalidating, ICacheInvalidatingByResult<DBRef>
{
	public string[] CacheKeys => [Definitions.CacheKeys.Contents(Location.Object().DBRef), Definitions.CacheKeys.Object(Creator.Object.DBRef)];

	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheTags.ExitList,
		Definitions.CacheTags.ObjectList,
		Definitions.CacheKeys.ContentsTag(Location.Object().DBRef.Number)
	];

	/// <summary>The dbref the write allocated may have been resolved, and cached as missing, before it existed.</summary>
	public string[] CacheKeysFor(DBRef created) => [Definitions.CacheKeys.Object(created)];
}
