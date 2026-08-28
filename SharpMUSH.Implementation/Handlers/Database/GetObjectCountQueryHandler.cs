using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetObjectCountQueryHandler(ISharpDatabase database) : IQueryHandler<GetObjectCountQuery, int>
{
	public async ValueTask<int> Handle(GetObjectCountQuery request, CancellationToken cancellationToken)
		=> await database.GetObjectCountAsync(cancellationToken);
}
