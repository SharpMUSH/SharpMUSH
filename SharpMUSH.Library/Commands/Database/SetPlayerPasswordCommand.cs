using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetPlayerPasswordCommand(SharpPlayer Player, string Password, string? Salt = null)
	: ICommand<ValueTask<Unit>>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Player.Object.DBRef)];

	public string[] CacheTags => [Definitions.CacheTags.PlayerNames];
}
