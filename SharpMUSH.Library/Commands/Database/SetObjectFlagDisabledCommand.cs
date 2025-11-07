using Mediator;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Command to enable or disable an object flag.
/// System flags cannot be disabled.
/// </summary>
/// <param name="Name">The name of the flag to update</param>
/// <param name="Disabled">True to disable the flag, false to enable it</param>
public record SetObjectFlagDisabledCommand(string Name, bool Disabled) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}
