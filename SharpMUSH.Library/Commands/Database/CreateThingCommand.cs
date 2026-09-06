using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateThingCommand(string Name, AnySharpContainer Where, SharpPlayer Owner, AnySharpContainer Home) : ICommand<DBRef>, ICacheInvalidating, ICacheInvalidatingByResult<DBRef>
{
	public string[] CacheKeys => [Definitions.CacheKeys.Contents(Where.Object().DBRef), Definitions.CacheKeys.Object(Owner.Object.DBRef), Definitions.CacheKeys.Object(Home.Object().DBRef)];

	public string[] CacheTags => [
		Definitions.CacheTags.ObjectList,
		Definitions.CacheTags.ThingList,
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheKeys.ContentsTag(Where.Object().DBRef.Number)
	];

	/// <summary>The dbref the write allocated may have been resolved, and cached as missing, before it existed.</summary>
	public string[] CacheKeysFor(DBRef created) => [Definitions.CacheKeys.Object(created)];
}