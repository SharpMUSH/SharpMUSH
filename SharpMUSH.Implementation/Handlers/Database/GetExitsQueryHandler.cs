using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetExitsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetExitsQuery, IAsyncEnumerable<SharpExit>?>
{
	public async ValueTask<IAsyncEnumerable<SharpExit>?> Handle(GetExitsQuery request, CancellationToken cancellationToken)
		=> await request.DBRef.Match<ValueTask<IAsyncEnumerable<SharpExit>?>>(
			async dbref  => await database.GetExitsAsync(dbref, cancellationToken),
			async obj => await database.GetExitsAsync(obj, cancellationToken)
			);
}
