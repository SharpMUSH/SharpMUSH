using Mediator;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Commands.Database;

public record DeleteObjectFlagCommand(string FlagName) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}
