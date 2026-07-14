using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetPlayerPasswordCommandHandler(ISharpDatabase database) : ICommandHandler<SetPlayerPasswordCommand, ValueTask<Unit>>
{
	public async ValueTask<ValueTask<Unit>> Handle(SetPlayerPasswordCommand command, CancellationToken cancellationToken)
	{
		await database.SetPlayerPasswordAsync(command.Player, command.Password, command.Salt, cancellationToken);

		// Setting God's (#1) character password is the classic PennMUSH way of claiming a
		// fresh game (@password / @newpassword) — it also completes first-run setup so the
		// web wizard closes. (The transparent legacy-rehash path only runs after a valid
		// non-empty password check, so it cannot fire on an unclaimed game.)
		if (command.Player.Object.Key == 1 && !string.IsNullOrEmpty(command.Password))
		{
			var state = await database.GetServerStateAsync(cancellationToken);
			if (!state.SetupCompleted)
				await database.SetServerSetupCompletedAsync(true, cancellationToken);
		}

		return ValueTask.FromResult(new Unit());
	}
}
