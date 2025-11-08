using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreatePowerCommand(
	string Name,
	string Alias,
	bool System,
	string[] SetPermissions,
	string[] UnsetPermissions,
	string[] TypeRestrictions
) : ICommand<SharpPower?>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.PowerList];
}
