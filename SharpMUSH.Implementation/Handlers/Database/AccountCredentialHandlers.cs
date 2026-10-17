using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Handlers.Database;

public class SetAccountCharacterPasswordLoginCommandHandler(IAccountStore store) : ICommandHandler<SetAccountCharacterPasswordLoginCommand>
{
	public async ValueTask<Unit> Handle(SetAccountCharacterPasswordLoginCommand command, CancellationToken cancellationToken)
	{
		await store.UpdateAccountCharacterPasswordLoginAsync(command.AccountId, command.Enabled, cancellationToken);
		return Unit.Value;
	}
}

public class RemoveAccountPasswordCommandHandler(IAccountStore store) : ICommandHandler<RemoveAccountPasswordCommand, Result<Success>>
{
	public ValueTask<Result<Success>> Handle(RemoveAccountPasswordCommand command, CancellationToken cancellationToken)
		=> store.TryRemoveAccountPasswordAsync(command.AccountId, command.EnabledRegistrations, cancellationToken);
}

public class RemoveAccountPasskeyCommandHandler(IAccountStore store) : ICommandHandler<RemoveAccountPasskeyCommand, Result<Success>>
{
	public ValueTask<Result<Success>> Handle(RemoveAccountPasskeyCommand command, CancellationToken cancellationToken)
		=> store.TryRemoveAccountPasskeyAsync(command.AccountId, command.CredentialId, command.EnabledRegistrations, cancellationToken);
}
