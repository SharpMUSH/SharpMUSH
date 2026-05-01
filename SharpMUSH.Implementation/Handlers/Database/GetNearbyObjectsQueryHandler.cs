using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetNearbyObjectsQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetNearbyObjectsQuery, AnySharpObject>
{
	public IAsyncEnumerable<AnySharpObject> Handle(GetNearbyObjectsQuery request, CancellationToken cancellationToken)
		=> request.DBRef.Value switch
		{
			DBRef dbref => database.GetNearbyObjectsAsync(dbref, cancellationToken),
			AnySharpObject obj => database.GetNearbyObjectsAsync(obj, cancellationToken),
			_ => throw new InvalidOperationException()
		};
}
