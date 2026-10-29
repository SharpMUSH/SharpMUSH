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

public class GetNextDbrefQueryHandler(IObjectStore database) : IQueryHandler<GetNextDbrefQuery, int>
{
	public async ValueTask<int> Handle(GetNextDbrefQuery request, CancellationToken cancellationToken)
		=> await database.GetNextDbrefAsync(cancellationToken);
}
