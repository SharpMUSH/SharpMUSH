using Mediator;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Commands.Database;

public record DeletePowerCommand(string PowerName) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.PowerList];
}
