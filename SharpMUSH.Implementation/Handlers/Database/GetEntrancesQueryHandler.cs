using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetEntrancesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetEntrancesQuery, SharpExit>
{
	public IAsyncEnumerable<SharpExit> Handle(GetEntrancesQuery request, CancellationToken cancellationToken)
		=> database.GetEntrancesAsync(request.Destination, cancellationToken);
}
