using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>
/// Streams stored log events, when the registered provider stores any.
/// </summary>
/// <remarks>
/// The dependency is the narrow <see cref="ISharpDatabaseWithLogging"/> rather than the whole
/// <see cref="ISharpDatabase"/> composite tested with an <c>is</c>: a handler depends on the store
/// surface it uses (engine data trunk §2), and nothing here needs the other twelve. It is optional
/// because log storage is genuinely optional — neither shipped provider implements the interface,
/// so the parameter's default is what DI supplies and the query streams nothing.
/// </remarks>
public class GetConnectionLogsQueryHandler(ISharpDatabaseWithLogging? logging = null)
	: IStreamQueryHandler<GetConnectionLogsQuery, LogEventEntity>
{
	public IAsyncEnumerable<LogEventEntity> Handle(GetConnectionLogsQuery request, CancellationToken cancellationToken)
		=> logging is null
			? AsyncEnumerable.Empty<LogEventEntity>()
			: SafeAsyncEnumerable(logging.GetLogsFromCategory(request.Category, request.Skip, request.Count));

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
