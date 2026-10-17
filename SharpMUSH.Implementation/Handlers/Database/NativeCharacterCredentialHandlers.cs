using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetNativeCharacterAuthorityQueryHandler(IObjectStore store)
	: IQueryHandler<GetNativeCharacterAuthorityQuery, Found<NativeCharacterAuthority>>
{
	public ValueTask<Found<NativeCharacterAuthority>> Handle(GetNativeCharacterAuthorityQuery query, CancellationToken cancellationToken)
		=> store.GetNativeCharacterAuthorityAsync(query.AccountId, query.Character, cancellationToken);
}

public class SetOwnedPlayerPasswordCommandHandler(IObjectStore store, IServerStateStore serverState)
	: ICommandHandler<SetOwnedPlayerPasswordCommand, Result<Success>>
{
	public async ValueTask<Result<Success>> Handle(SetOwnedPlayerPasswordCommand command, CancellationToken cancellationToken)
	{
		var result = await store.SetOwnedPlayerPasswordAsync(command.AccountId, command.Character, command.PasswordHash, cancellationToken);
		if (result is Success && command.Character.Number == 1)
		{
			var state = await serverState.GetServerStateAsync(cancellationToken);
			if (!state.SetupCompleted) await serverState.SetServerSetupCompletedAsync(true, cancellationToken);
		}
		return result;
	}
}
