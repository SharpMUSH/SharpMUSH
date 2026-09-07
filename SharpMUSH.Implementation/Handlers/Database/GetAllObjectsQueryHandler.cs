using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAllObjectsQueryHandler(IObjectStore database)
	: IStreamQueryHandler<GetAllObjectsQuery, SharpObject>
{
	public IAsyncEnumerable<SharpObject> Handle(GetAllObjectsQuery request, CancellationToken cancellationToken)
		=> database.GetAllObjectsAsync(cancellationToken);
}
