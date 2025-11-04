using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAllObjectsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetAllObjectsQuery, IAsyncEnumerable<SharpObject>>
{
	public async ValueTask<IAsyncEnumerable<SharpObject>> Handle(GetAllObjectsQuery request, CancellationToken cancellationToken)
		=> await Task.FromResult(database.GetAllObjectsAsync(cancellationToken));
}
