using SharpMUSH.Library.Models;
using SurrealDb.Net.Models;

namespace SharpMUSH.Database.SurrealDB;

public partial class SurrealDatabase
{
	// SurrealDb.Net deserializes by exact (case-sensitive) field name and does NOT honor
	// [JsonPropertyName]; property names must match the stored camelCase fields verbatim, same
	// rule as AccountDbRecord.
	internal class ServerStateDbRecord : Record
	{
		public bool setupCompleted { get; set; }
	}

	public async ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT * FROM server_state:state",
			new Dictionary<string, object?>(), cancellationToken);
		var rows = response.GetValue<List<ServerStateDbRecord>>(0);
		return new SharpServerState { SetupCompleted = rows?.FirstOrDefault()?.setupCompleted ?? false };
	}

	public async ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
	{
		await ExecuteAsync("UPSERT server_state:state SET setupCompleted = $value",
			new Dictionary<string, object?> { ["value"] = value }, cancellationToken);
	}

	// TEMPORARY: implemented in Task 3.
	public ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
}
