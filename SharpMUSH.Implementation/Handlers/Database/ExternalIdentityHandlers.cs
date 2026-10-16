using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetExternalIdentityQueryHandler(IExternalIdentityStore store) : IQueryHandler<GetExternalIdentityQuery, Found<ExternalIdentity>>
{
	public ValueTask<Found<ExternalIdentity>> Handle(GetExternalIdentityQuery query, CancellationToken cancellationToken)
		=> store.GetExternalIdentityAsync(query.Key, cancellationToken);
}

public class GetExternalIdentitiesQueryHandler(IExternalIdentityStore store) : IQueryHandler<GetExternalIdentitiesQuery, IReadOnlyList<ExternalIdentity>>
{
	public ValueTask<IReadOnlyList<ExternalIdentity>> Handle(GetExternalIdentitiesQuery query, CancellationToken cancellationToken)
		=> store.GetExternalIdentitiesAsync(query.AccountId, cancellationToken);
}

public class ProvisionExternalAccountCommandHandler(IExternalIdentityStore store) : ICommandHandler<ProvisionExternalAccountCommand, Result<SharpAccount>>
{
	public ValueTask<Result<SharpAccount>> Handle(ProvisionExternalAccountCommand command, CancellationToken cancellationToken)
		=> store.ProvisionExternalAccountAsync(command.Key, command.Username, command.Label, cancellationToken);
}

public class AttachExternalIdentityCommandHandler(IExternalIdentityStore store) : ICommandHandler<AttachExternalIdentityCommand, Result<ExternalIdentity>>
{
	public ValueTask<Result<ExternalIdentity>> Handle(AttachExternalIdentityCommand command, CancellationToken cancellationToken)
		=> store.AttachExternalIdentityAsync(command.AccountId, command.Key, command.Label, cancellationToken);
}

public class RecordExternalIdentityUseCommandHandler(IExternalIdentityStore store) : ICommandHandler<RecordExternalIdentityUseCommand, bool>
{
	public ValueTask<bool> Handle(RecordExternalIdentityUseCommand command, CancellationToken cancellationToken)
		=> store.RecordExternalIdentityUseAsync(command.AccountId, command.Key, command.UsedAt, cancellationToken);
}

public class RemoveExternalIdentityCommandHandler(IExternalIdentityStore store) : ICommandHandler<RemoveExternalIdentityCommand, Result<Success>>
{
	public ValueTask<Result<Success>> Handle(RemoveExternalIdentityCommand command, CancellationToken cancellationToken)
		=> store.RemoveExternalIdentityAsync(command.AccountId, command.Key, command.EnabledRegistrations, cancellationToken);
}
