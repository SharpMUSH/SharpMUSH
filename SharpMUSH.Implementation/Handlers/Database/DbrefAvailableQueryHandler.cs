using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class DbrefAvailableQueryHandler(IObjectStore database) : IQueryHandler<DbrefAvailableQuery, bool>
{
	public async ValueTask<bool> Handle(DbrefAvailableQuery request, CancellationToken cancellationToken)
		=> await database.IsDbrefAvailableAsync(request.Requested, cancellationToken);
}
