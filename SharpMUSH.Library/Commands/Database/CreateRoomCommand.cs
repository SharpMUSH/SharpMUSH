using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>Creates a room and stamps the configured default room flags on it.</summary>
/// <param name="ApplyDefaultFlags">
/// Whether the configured default flags for this type are stamped on the new object. True for
/// anything a player creates. False for a package install, where the manifest is the whole truth
/// about the object's flags: <c>room_flags</c> defaults to nothing by default, but a game may set it.
/// </param>
public record CreateRoomCommand(string Name, SharpPlayer Creator, bool ApplyDefaultFlags = true) : ICommand<DBRef>, ICacheInvalidating, ICacheInvalidatingByResult<DBRef>
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Creator.Object.DBRef)];

	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheTags.ObjectList,
		Definitions.CacheTags.RoomList
	];

	/// <summary>The dbref the write allocated may have been resolved, and cached as missing, before it existed.</summary>
	public string[] CacheKeysFor(DBRef created) => [Definitions.CacheKeys.Object(created)];
}
