using Mediator;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Library.Queries.Database;

/// <summary>
/// Query to retrieve connection logs from the database.
/// </summary>
/// <param name="Category">The log category to filter by (e.g., "Connection")</param>
/// <param name="Skip">Number of records to skip for pagination</param>
/// <param name="Count">Number of records to return</param>
public record GetConnectionLogsQuery(string Category, int Skip = 0, int Count = 100) 
	: IStreamQuery<LogEventEntity>, ICacheable
{
	public string CacheKey => $"logs:{Category}:{Skip}:{Count}";
	public string[] CacheTags => [Definitions.CacheTags.ConnectionLogs];
}
