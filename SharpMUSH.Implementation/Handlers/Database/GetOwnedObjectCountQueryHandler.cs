using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetOwnedObjectCountQueryHandler(ISharpDatabase database) : IQueryHandler<GetOwnedObjectCountQuery, int>
{
	public async ValueTask<int> Handle(GetOwnedObjectCountQuery request, CancellationToken cancellationToken)
		=> await database.GetOwnedObjectCountAsync(request.Player, cancellationToken);
}
