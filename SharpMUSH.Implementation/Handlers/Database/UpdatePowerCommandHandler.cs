using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class UpdatePowerCommandHandler(IFlagAndPowerStore database) : ICommandHandler<UpdatePowerCommand, bool>
{
	public async ValueTask<bool> Handle(UpdatePowerCommand command, CancellationToken cancellationToken)
	{
		return await database.UpdatePowerAsync(
			command.Name,
			command.Alias,
			command.Symbol,
			command.SetPermissions,
			command.UnsetPermissions,
			command.TypeRestrictions,
			cancellationToken
		);
	}
}
