using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetExitsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetExitsQuery, IEnumerable<SharpExit>?>
{
	public async ValueTask<IEnumerable<SharpExit>?> Handle(GetExitsQuery request, CancellationToken cancellationToken)
		=> await request.DBRef.Match<ValueTask<IEnumerable<SharpExit>?>>(
			database.GetExitsAsync,
			async obj => await database.GetExitsAsync(obj)
			);
}
