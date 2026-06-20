using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record LinkRoomCommand(SharpRoom Room, AnyOptionalSharpContainer Location) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Room.Object.DBRef)];
	public string[] CacheTags => [];
}

public record UnlinkRoomCommand(SharpRoom Room) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Room.Object.DBRef)];
	public string[] CacheTags => [];
}
