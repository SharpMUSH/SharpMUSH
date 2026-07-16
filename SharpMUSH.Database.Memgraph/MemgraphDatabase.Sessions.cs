using Neo4j.Driver;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	#region Sessions

	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync(
			"MERGE (s:Session {token: $token}) SET s.accountId = $accountId, s.expiryUnixMs = $expiryUnixMs, s.ttlMs = $ttlMs, s.originIp = $originIp",
			new
			{
				token = session.Token,
				accountId = session.AccountId,
				expiryUnixMs = session.ExpiryUnixMs,
				ttlMs = session.TtlMs,
				originIp = session.OriginIp
			}, cancellationToken);
	}

	public async ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (s:Session {token: $token}) RETURN s", new { token }, cancellationToken);
		if (result.Result.Count == 0)
			return null;

		var node = result.Result[0]["s"].As<INode>();
		return new SharpSession
		{
			Token = token,
			AccountId = node.Properties["accountId"].As<string>(),
			ExpiryUnixMs = Convert.ToInt64(node.Properties["expiryUnixMs"]),
			TtlMs = Convert.ToInt64(node.Properties["ttlMs"]),
			OriginIp = node.Properties["originIp"].As<string>()
		};
	}

	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync(
			"MATCH (s:Session {token: $token}) DELETE s", new { token }, cancellationToken);
	}

	public async ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync(
			"MATCH (s:Session {accountId: $accountId}) DELETE s", new { accountId }, cancellationToken);
	}

	public async ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync(
			"MATCH (s:Session {originIp: $originIp}) DELETE s", new { originIp }, cancellationToken);
	}

	#endregion
}
