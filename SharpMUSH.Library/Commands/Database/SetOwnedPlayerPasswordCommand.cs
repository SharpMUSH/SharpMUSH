using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record SetOwnedPlayerPasswordCommand(string AccountId, DBRef Character, string PasswordHash)
	: ICommand<Result<Success>>, ICacheInvalidating
{
	public string[] CacheKeys => [Definitions.CacheKeys.Object(Character)];
	public string[] CacheTags => [Definitions.CacheTags.PlayerNames];
}
