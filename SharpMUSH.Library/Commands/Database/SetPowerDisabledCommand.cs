using Mediator;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Commands.Database;

/// <summary>
/// Command to enable or disable a power.
/// System powers cannot be disabled.
/// </summary>
/// <param name="Name">The name of the power to update</param>
/// <param name="Disabled">True to disable the power, false to enable it</param>
public record SetPowerDisabledCommand(string Name, bool Disabled) : ICommand<bool>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.PowerList];
}
