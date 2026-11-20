using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAccountBucketQueryHandler(IAccountBucketStore store) : IQueryHandler<GetAccountBucketQuery, Found<string>>
{
	public ValueTask<Found<string>> Handle(GetAccountBucketQuery query, CancellationToken cancellationToken)
		=> store.GetAccountBucketAsync(query.AccountId, query.Name, cancellationToken);
}

public class GetAccountBucketNamesQueryHandler(IAccountBucketStore store)
	: IQueryHandler<GetAccountBucketNamesQuery, IReadOnlyList<string>>
{
	public ValueTask<IReadOnlyList<string>> Handle(GetAccountBucketNamesQuery query, CancellationToken cancellationToken)
		=> store.GetAccountBucketNamesAsync(query.AccountId, cancellationToken);
}

public class SetAccountBucketCommandHandler(IAccountBucketStore store) : ICommandHandler<SetAccountBucketCommand, AccountBucketWrite>
{
	public ValueTask<AccountBucketWrite> Handle(SetAccountBucketCommand command, CancellationToken cancellationToken)
		=> store.SetAccountBucketAsync(command.AccountId, command.Name, command.Json, AccountBuckets.MaxPerAccount, cancellationToken);
}

public class RemoveAccountBucketCommandHandler(IAccountBucketStore store) : ICommandHandler<RemoveAccountBucketCommand, bool>
{
	public ValueTask<bool> Handle(RemoveAccountBucketCommand command, CancellationToken cancellationToken)
		=> store.RemoveAccountBucketAsync(command.AccountId, command.Name, cancellationToken);
}
