using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class UpdatePowerCommandHandler(ISharpDatabase database) : ICommandHandler<UpdatePowerCommand, bool>
{
	public async ValueTask<bool> Handle(UpdatePowerCommand command, CancellationToken cancellationToken)
	{
		return await database.UpdatePowerAsync(
			command.Name,
			command.Alias,
			command.SetPermissions,
			command.UnsetPermissions,
			command.TypeRestrictions,
			cancellationToken
		);
	}
}
