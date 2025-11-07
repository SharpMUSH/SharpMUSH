using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class UpdateObjectFlagCommandHandler(ISharpDatabase database) : ICommandHandler<UpdateObjectFlagCommand, bool>
{
	public async ValueTask<bool> Handle(UpdateObjectFlagCommand command, CancellationToken cancellationToken)
	{
		return await database.UpdateObjectFlagAsync(
			command.Name,
			command.Aliases,
			command.Symbol,
			command.SetPermissions,
			command.UnsetPermissions,
			command.TypeRestrictions,
			cancellationToken
		);
	}
}
