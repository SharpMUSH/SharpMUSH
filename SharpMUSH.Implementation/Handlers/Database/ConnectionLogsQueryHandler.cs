using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetConnectionLogsQueryHandler(ISharpDatabase database) 
	: IQueryHandler<GetConnectionLogsQuery, IAsyncEnumerable<LogEventEntity>>
{
	public async ValueTask<IAsyncEnumerable<LogEventEntity>> Handle(GetConnectionLogsQuery request, CancellationToken cancellationToken)
	{
		if (database is ISharpDatabaseWithLogging loggingDb)
		{
			return loggingDb.GetLogsFromCategory(request.Category, request.Skip, request.Count);
		}
		
		// Return empty enumerable if logging is not supported
		return AsyncEnumerable.Empty<LogEventEntity>();
	}
}
