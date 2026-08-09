using Neo4j.Driver;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.Memgraph;

public partial class MemgraphDatabase
{
	#region Sessions

	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
	{
		await ExecuteWithRetryAsync(
			"MERGE (s:Session {token: $token}) SET s.accountId = $accountId, s.expiryUnixMs = $expiryUnixMs, s.ttlMs = $ttlMs, s.originIp = $originIp, s.characterKey = $characterKey, s.characterCreationTime = $characterCreationTime",
			new
			{
				token = session.Token,
				accountId = session.AccountId,
				expiryUnixMs = session.ExpiryUnixMs,
				ttlMs = session.TtlMs,
				originIp = session.OriginIp,
				// Setting a property to null removes it, which is exactly the "acts as nobody" state.
				characterKey = session.CharacterKey,
				characterCreationTime = session.CharacterCreationTime
			}, cancellationToken);
	}

	/// <summary>
	/// MATCH, never MERGE: the pattern binds nothing when the node is gone, so the SET never runs and no
	/// node is created. <see cref="ExecuteWithRetryAsync"/> already retries the MVCC conflict a concurrent
	/// revoke can raise, so a lost race here retries and then correctly finds nothing.
	/// </summary>
	public async ValueTask<bool> TouchSessionExpiryAsync(string token, long expiryUnixMs,
		CancellationToken cancellationToken = default)
	{
		var result = await ExecuteWithRetryAsync(
			"MATCH (s:Session {token: $token}) SET s.expiryUnixMs = $expiryUnixMs RETURN s.token AS token",
			new { token, expiryUnixMs }, cancellationToken);
		return result.Result.Count > 0;
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
			OriginIp = node.Properties["originIp"].As<string>(),
			CharacterKey = node.Properties.TryGetValue("characterKey", out var ck) && ck is not null
				? Convert.ToInt32(ck) : null,
			CharacterCreationTime = node.Properties.TryGetValue("characterCreationTime", out var cc) && cc is not null
				? Convert.ToInt64(cc) : null
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
