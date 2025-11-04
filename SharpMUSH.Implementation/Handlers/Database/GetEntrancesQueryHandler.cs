using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetEntrancesQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetEntrancesQuery, IAsyncEnumerable<SharpExit>>
{
	public async ValueTask<IAsyncEnumerable<SharpExit>> Handle(GetEntrancesQuery request, CancellationToken cancellationToken)
		=> await database.GetEntrancesAsync(request.Destination, cancellationToken);
}
