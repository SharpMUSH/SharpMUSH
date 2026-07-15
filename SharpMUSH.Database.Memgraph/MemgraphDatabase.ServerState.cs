using Neo4j.Driver;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	public async ValueTask<SharpServerState> GetServerStateAsync(CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (s:ServerState {id: 'state'}) RETURN s", new { }, cancellationToken);
		if (result.Result.Count == 0)
			return new SharpServerState();

		var node = result.Result[0]["s"].As<INode>();
		return new SharpServerState
		{
			SetupCompleted = node.Properties.TryGetValue("setupCompleted", out var value) && (bool)value
		};
	}

	public async ValueTask SetServerSetupCompletedAsync(bool value, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync(
			"MERGE (s:ServerState {id: 'state'}) SET s.setupCompleted = $value",
			new { value }, cancellationToken);
	}

	// TEMPORARY: implemented in Task 3.
	public ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
	public ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default) => throw new NotImplementedException("Task 3");
}
