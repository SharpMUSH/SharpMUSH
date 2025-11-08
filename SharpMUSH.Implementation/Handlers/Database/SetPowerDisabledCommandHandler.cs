using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetPowerDisabledCommandHandler(ISharpDatabase database)
	: ICommandHandler<SetPowerDisabledCommand, bool>
{
	public async ValueTask<bool> Handle(SetPowerDisabledCommand command, CancellationToken cancellationToken)
	{
		return await database.SetPowerDisabledAsync(command.Name, command.Disabled, cancellationToken);
	}
}
