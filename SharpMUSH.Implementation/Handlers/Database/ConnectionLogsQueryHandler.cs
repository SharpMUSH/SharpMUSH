using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetConnectionLogsQueryHandler(ISharpDatabase database) 
	: IStreamQueryHandler<GetConnectionLogsQuery, LogEventEntity>
{
	public IAsyncEnumerable<LogEventEntity> Handle(GetConnectionLogsQuery request, CancellationToken cancellationToken)
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
		var enumerator = source.GetAsyncEnumerator();
		
		try
		{
			while (true)
			{
				bool hasNext;
				try
				{
					hasNext = await enumerator.MoveNextAsync();
				}
				catch
				{
					yield break;
				}
				
				if (!hasNext) yield break;
				
				yield return enumerator.Current;
			}
		}
		finally
		{
			await enumerator.DisposeAsync();
		}
	}
}
