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

public class GetAccountValueRulesQueryHandler(IAccountValueStore store)
	: IQueryHandler<GetAccountValueRulesQuery, IReadOnlyList<AccountValueRule>>
{
	public ValueTask<IReadOnlyList<AccountValueRule>> Handle(GetAccountValueRulesQuery query, CancellationToken cancellationToken)
		=> store.GetAccountValueRulesAsync(cancellationToken);
}

public class SetAccountValueCommandHandler(IAccountValueStore store) : ICommandHandler<SetAccountValueCommand, AccountValueWrite>
{
	public ValueTask<AccountValueWrite> Handle(SetAccountValueCommand command, CancellationToken cancellationToken)
		=> store.SetAccountValueAsync(command.AccountId, command.Name, command.Json, command.Expected, AccountValues.MaxPerAccount,
			cancellationToken);
}

public class RemoveAccountValueCommandHandler(IAccountValueStore store) : ICommandHandler<RemoveAccountValueCommand, AccountValueChange>
{
	public ValueTask<AccountValueChange> Handle(RemoveAccountValueCommand command, CancellationToken cancellationToken)
		=> store.RemoveAccountValueAsync(command.AccountId, command.Name, command.Expected, cancellationToken);
}

public class SetAccountValueLocksCommandHandler(IAccountValueStore store)
	: ICommandHandler<SetAccountValueLocksCommand, AccountValueChange>
{
	public ValueTask<AccountValueChange> Handle(SetAccountValueLocksCommand command, CancellationToken cancellationToken)
		=> store.SetAccountValueLocksAsync(command.AccountId, command.Name, command.Expected, command.Locks, cancellationToken);
}

public class SetAccountValueRuleCommandHandler(IAccountValueStore store) : ICommandHandler<SetAccountValueRuleCommand>
{
	public async ValueTask<Unit> Handle(SetAccountValueRuleCommand command, CancellationToken cancellationToken)
	{
		await store.SetAccountValueRuleAsync(command.Pattern, command.Locks, cancellationToken);
		return Unit.Value;
	}
}
