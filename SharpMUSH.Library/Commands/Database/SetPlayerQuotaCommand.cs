using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetPlayerQuotaCommand(SharpPlayer Player, int Quota) : ICommand, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Player.Object.DBRef)];

	public string[] CacheTags => [Definitions.CacheTags.PlayerList, Definitions.CacheTags.PlayerNames];
}
