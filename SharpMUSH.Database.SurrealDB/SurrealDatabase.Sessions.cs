using SharpMUSH.Library.Models;
using SurrealDb.Net.Models;

namespace SharpMUSH.Database.SurrealDB;

public partial class SurrealDatabase
{
	#region Sessions

	// SurrealDb.Net deserializes by exact (case-sensitive) field name and does NOT honor
	// [JsonPropertyName]; property names must match the stored camelCase fields verbatim, same
	// rule as AccountDbRecord / ServerStateDbRecord.
	internal class SessionDbRecord : Record
	{
		public string accountId { get; set; } = "";
		public long expiryUnixMs { get; set; }
		public long ttlMs { get; set; }
		public string originIp { get; set; } = "";
	}

	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?>
		{
			["token"] = session.Token,
			["accountId"] = session.AccountId,
			["expiryUnixMs"] = session.ExpiryUnixMs,
			["ttlMs"] = session.TtlMs,
			["originIp"] = session.OriginIp
		};
		await ExecuteAsync(
			"UPSERT type::thing('session', $token) SET accountId = $accountId, expiryUnixMs = $expiryUnixMs, ttlMs = $ttlMs, originIp = $originIp",
			parameters, cancellationToken);
	}

	public async ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync(
			"SELECT * FROM type::thing('session', $token)",
			new Dictionary<string, object?> { ["token"] = token }, cancellationToken);
		var results = response.GetValue<List<SessionDbRecord>>(0);
		if (results is not { Count: > 0 })
			return null;

		var row = results[0];
		return new SharpSession
		{
			Token = token,
			AccountId = row.accountId,
			ExpiryUnixMs = row.expiryUnixMs,
			TtlMs = row.ttlMs,
			OriginIp = row.originIp
		};
	}

	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		await ExecuteAsync("DELETE type::thing('session', $token)",
			new Dictionary<string, object?> { ["token"] = token }, cancellationToken);
	}

	public async ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		await ExecuteAsync("DELETE session WHERE accountId = $accountId",
			new Dictionary<string, object?> { ["accountId"] = accountId }, cancellationToken);
	}

	public async ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
	{
		await ExecuteAsync("DELETE session WHERE originIp = $originIp",
			new Dictionary<string, object?> { ["originIp"] = originIp }, cancellationToken);
	}

	#endregion
}
