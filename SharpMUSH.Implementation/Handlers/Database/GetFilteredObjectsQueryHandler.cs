using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetFilteredObjectsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetFilteredObjectsQuery, SharpObject>
{
	public IAsyncEnumerable<SharpObject> Handle(GetFilteredObjectsQuery request, CancellationToken cancellationToken)
	{
		if (request.Filter == null || !request.Filter.HasFilters)
		{
			return database.GetAllObjectsAsync(cancellationToken);
		}

		return database.GetFilteredObjectsAsync(request.Filter, cancellationToken);
	}
}
