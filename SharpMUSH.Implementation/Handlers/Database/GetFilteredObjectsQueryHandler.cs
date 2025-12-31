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
		// If no filter is provided, return all objects
		if (request.Filter == null || !request.Filter.HasFilters)
		{
			return database.GetAllObjectsAsync(cancellationToken);
		}

		// Otherwise, use the filtered query method
		return database.GetFilteredObjectsAsync(request.Filter, cancellationToken);
	}
}
