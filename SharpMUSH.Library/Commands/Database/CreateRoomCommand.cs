using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateRoomCommand(string Name, SharpPlayer Creator) : ICommand<DBRef>, ICacheInvalidating
{
	public string[] CacheKeys => [Creator.Object.DBRef.ToString()];

	public string[] CacheTags =>
	[
		Definitions.CacheTags.ObjectOwnership,
		Definitions.CacheTags.ObjectList,
		Definitions.CacheTags.RoomList
	];
}