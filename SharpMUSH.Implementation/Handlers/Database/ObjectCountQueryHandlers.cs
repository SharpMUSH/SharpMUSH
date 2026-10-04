using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetChildCountQueryHandler(IObjectStore database) : IQueryHandler<GetChildCountQuery, int>
{
	public async ValueTask<int> Handle(GetChildCountQuery request, CancellationToken cancellationToken)
		=> await database.GetChildCountAsync(request.Parent, cancellationToken);
}

public class GetObjectTypeCountsQueryHandler(IObjectStore database) : IQueryHandler<GetObjectTypeCountsQuery, ObjectTypeCounts>
{
	public async ValueTask<ObjectTypeCounts> Handle(GetObjectTypeCountsQuery request, CancellationToken cancellationToken)
		=> await database.GetObjectTypeCountsAsync(request.Owner, cancellationToken);
}

public class GetHighestDbrefQueryHandler(IObjectStore database) : IQueryHandler<GetHighestDbrefQuery, Found<int>>
{
	public async ValueTask<Found<int>> Handle(GetHighestDbrefQuery request, CancellationToken cancellationToken)
		=> await database.GetHighestDbrefAsync(cancellationToken);
}
