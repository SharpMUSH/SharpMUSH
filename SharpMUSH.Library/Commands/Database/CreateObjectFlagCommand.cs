using Mediator;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Commands.Database;

public record CreateObjectFlagCommand(
	string Name,
	string[]? Aliases,
	string Symbol,
	bool System,
	string[] SetPermissions,
	string[] UnsetPermissions,
	string[] TypeRestrictions
) : ICommand<SharpObjectFlag?>, ICacheInvalidating
{
	public string[] CacheKeys => [];
	public string[] CacheTags => [Definitions.CacheTags.FlagList];
}
