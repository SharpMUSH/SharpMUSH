using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetNearbyObjectsQueryHandler(ISharpDatabase database)
	: IQueryHandler<GetNearbyObjectsQuery, IAsyncEnumerable<AnySharpObject>>
{
	public async ValueTask<IAsyncEnumerable<AnySharpObject>> Handle(GetNearbyObjectsQuery request, CancellationToken cancellationToken)
		=> await request.DBRef.Match(
			async dbRef => await database.GetNearbyObjectsAsync(dbRef),
			async obj => await database.GetNearbyObjectsAsync(obj)
			);
}
