using Mediator;

namespace SharpMUSH.Library.Commands.Database;

public record UpdatePowerCommand(
	string Name,
	string Alias,
	string[] SetPermissions,
	string[] UnsetPermissions,
	string[] TypeRestrictions
) : ICommand<bool>;
