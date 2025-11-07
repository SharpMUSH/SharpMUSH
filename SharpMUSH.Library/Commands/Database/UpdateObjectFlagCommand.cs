using Mediator;

namespace SharpMUSH.Library.Commands.Database;

public record UpdateObjectFlagCommand(
	string Name,
	string[]? Aliases,
	string Symbol,
	string[] SetPermissions,
	string[] UnsetPermissions,
	string[] TypeRestrictions
) : ICommand<bool>;
