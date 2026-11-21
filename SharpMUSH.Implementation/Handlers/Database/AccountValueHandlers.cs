using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAccountValueQueryHandler(IAccountValueStore store) : IQueryHandler<GetAccountValueQuery, Found<AccountValue>>
{
	public ValueTask<Found<AccountValue>> Handle(GetAccountValueQuery query, CancellationToken cancellationToken)
		=> store.GetAccountValueAsync(query.AccountId, query.Name, cancellationToken);
}

public class GetAccountValuesQueryHandler(IAccountValueStore store) : IQueryHandler<GetAccountValuesQuery, IReadOnlyList<AccountValue>>
{
	public ValueTask<IReadOnlyList<AccountValue>> Handle(GetAccountValuesQuery query, CancellationToken cancellationToken)
		=> store.GetAccountValuesAsync(query.AccountId, cancellationToken);
}

public class SetAccountValueCommandHandler(IAccountValueStore store) : ICommandHandler<SetAccountValueCommand, AccountValueWrite>
{
	public ValueTask<AccountValueWrite> Handle(SetAccountValueCommand command, CancellationToken cancellationToken)
		=> store.SetAccountValueAsync(command.AccountId, command.Name, command.Json, command.Writer, command.OverrideLock,
			AccountValues.MaxPerAccount, cancellationToken);
}

public class RemoveAccountValueCommandHandler(IAccountValueStore store) : ICommandHandler<RemoveAccountValueCommand, AccountValueChange>
{
	public ValueTask<AccountValueChange> Handle(RemoveAccountValueCommand command, CancellationToken cancellationToken)
		=> store.RemoveAccountValueAsync(command.AccountId, command.Name, command.Writer, command.OverrideLock, cancellationToken);
}

public class SetAccountValueLockCommandHandler(IAccountValueStore store) : ICommandHandler<SetAccountValueLockCommand, AccountValueChange>
{
	public ValueTask<AccountValueChange> Handle(SetAccountValueLockCommand command, CancellationToken cancellationToken)
		=> store.SetAccountValueLockAsync(command.AccountId, command.Name, command.Writer, command.Locked, command.OverrideLock,
			cancellationToken);
}
