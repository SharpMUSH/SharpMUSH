using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;
public class SetPlayerPasswordCommandHandler(ISharpDatabase database) : ICommandHandler<SetPlayerPasswordCommand, ValueTask<Unit>>
{
	public async ValueTask<ValueTask<Unit>> Handle(SetPlayerPasswordCommand command, CancellationToken cancellationToken)
	{
		await database.SetPlayerPasswordAsync(command.Player, command.Password);
		return ValueTask.FromResult(new Unit());
	}
}
