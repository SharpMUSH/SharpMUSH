using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>Creates an exit and stamps the configured default exit flags on it.</summary>
/// <param name="ApplyDefaultFlags">
/// Whether the configured default flags for this type are stamped on the new object. True for
/// anything a player creates. False for an import, whose objects are the source database's rather
/// than new ones here, and for a package install, where the manifest is the whole truth about the
/// object's flags: <c>exit_flags</c> defaults to nothing by default, but a game may set it.
/// </param>
/// <param name="CreationTime">
/// Creation time in Unix milliseconds, or <c>null</c> for now. An import passes the source's, since the
/// objid is <c>#N:&lt;creation time&gt;</c> and softcode in the imported database holds those.
/// </param>
/// <param name="ModifiedTime">Modification time in Unix milliseconds, or <c>null</c> to match the creation time.</param>
public record CreateExitCommand(string Name, string[] Aliases, AnySharpContainer Location, SharpPlayer Creator, bool ApplyDefaultFlags = true,
	long? CreationTime = null, long? ModifiedTime = null)
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
