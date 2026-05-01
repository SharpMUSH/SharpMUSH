using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetNearbyObjectsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetNearbyObjectsQuery, AnySharpObject>
{
	public IAsyncEnumerable<AnySharpObject> Handle(GetNearbyObjectsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Match(
			dbref => database.GetNearbyObjectsAsync(dbref, cancellationToken),
			obj   => database.GetNearbyObjectsAsync(obj, cancellationToken));
}
