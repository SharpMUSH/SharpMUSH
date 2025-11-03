using System.Text.Json.Serialization;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

public interface ISharpDatabaseWithLogging
{
	ValueTask SetupLogging();
	
	IAsyncEnumerable<LogEventEntity> GetChannelLogs(SharpChannel channel, int skip = 0, int count = 100);
	
	IAsyncEnumerable<LogEventEntity> GetLogsFromCategory(string category, int skip = 0, int count = 100);
}

public class LogEventEntity
{
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public required string Key { get; set; }

	public required DateTime Timestamp { get; set; }

	public required string Level { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public required string Message { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public required string MessageTemplate { get; set; }

	public required string Exception { get; set; }

	public required Dictionary<string, string> Properties { get; set; } = [];
}