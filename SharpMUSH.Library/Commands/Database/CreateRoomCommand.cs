using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateRoomCommand(string Name, SharpPlayer Creator) : ICommand<DBRef>, ICacheInvalidating, ICacheInvalidatingByResult<DBRef>
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
