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
			return SafeAsyncEnumerable(loggingDb.GetLogsFromCategory(request.Category, request.Skip, request.Count));
		}
		
		// Return empty enumerable if logging is not supported
		return AsyncEnumerable.Empty<LogEventEntity>();
	}
	
	private static async IAsyncEnumerable<LogEventEntity> SafeAsyncEnumerable(IAsyncEnumerable<LogEventEntity> source)
	{
		IAsyncEnumerator<LogEventEntity>? enumerator = null;
		try
		{
			enumerator = source.GetAsyncEnumerator();
			while (true)
			{
				bool hasNext;
				try
				{
					hasNext = await enumerator.MoveNextAsync();
				}
				catch
				{
					// If enumeration fails, stop silently
					yield break;
				}
				
				if (!hasNext) yield break;
				
				yield return enumerator.Current;
			}
		}
		finally
		{
			if (enumerator != null)
			{
				await enumerator.DisposeAsync();
			}
		}
	}
}
