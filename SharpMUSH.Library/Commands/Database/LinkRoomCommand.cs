using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record LinkRoomCommand(SharpRoom Room, AnyOptionalSharpContainer DropTo) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [$"object:{Room.Object.DBRef}"];
	public string[] CacheTags => [];
}

public record UnlinkRoomCommand(SharpRoom Room) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [$"object:{Room.Object.DBRef}"];
	public string[] CacheTags => [];
}
