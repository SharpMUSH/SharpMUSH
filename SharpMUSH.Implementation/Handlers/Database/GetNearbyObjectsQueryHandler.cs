using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetNearbyObjectsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetNearbyObjectsQuery, AnySharpObject>
{
	public IAsyncEnumerable<AnySharpObject> Handle(GetNearbyObjectsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Match<IAsyncEnumerable<AnySharpObject>>(
			dbrefQuery => database.GetNearbyObjectsAsync(dbrefQuery, cancellationToken),
			objQuery => database.GetNearbyObjectsAsync(objQuery, cancellationToken));
}
