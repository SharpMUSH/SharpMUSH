using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetServerSetupCompletedCommandHandler(ISharpDatabase database) : ICommandHandler<SetServerSetupCompletedCommand, Unit>
{
	public async ValueTask<Unit> Handle(SetServerSetupCompletedCommand command, CancellationToken cancellationToken)
	{
		await database.SetServerSetupCompletedAsync(command.Value, cancellationToken);
		return new Unit();
	}
}
