using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetObjectFlagDisabledCommandHandler(ISharpDatabase database)
	: ICommandHandler<SetObjectFlagDisabledCommand, bool>
{
	public async ValueTask<bool> Handle(SetObjectFlagDisabledCommand command, CancellationToken cancellationToken)
	{
		return await database.SetObjectFlagDisabledAsync(command.Name, command.Disabled, cancellationToken);
	}
}
