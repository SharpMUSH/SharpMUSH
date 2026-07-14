using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetServerStateQueryHandler(ISharpDatabase database) : IQueryHandler<GetServerStateQuery, SharpServerState>
{
	public async ValueTask<SharpServerState> Handle(GetServerStateQuery query, CancellationToken cancellationToken)
		=> await database.GetServerStateAsync(cancellationToken);
}
