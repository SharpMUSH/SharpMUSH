using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetObjectGrantsQueryHandler(IRoleRegistryService roles, IAccountStore accounts)
	: IQueryHandler<GetObjectGrantsQuery, ObjectGrants>
{
	public async ValueTask<ObjectGrants> Handle(GetObjectGrantsQuery query, CancellationToken cancellationToken)
		=> await ObjectGrantsReader.ReadAsync(roles, accounts, query.Number, query.IsPlayer, cancellationToken);
}
