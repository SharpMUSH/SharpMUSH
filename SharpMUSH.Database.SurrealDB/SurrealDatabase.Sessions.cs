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
		public int? characterKey { get; set; }
		public long? characterCreationTime { get; set; }
	}

	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
	{
		var parameters = new Dictionary<string, object?>
		{
			["token"] = session.Token,
			["accountId"] = session.AccountId,
			["expiryUnixMs"] = session.ExpiryUnixMs,
			["ttlMs"] = session.TtlMs,
			["originIp"] = session.OriginIp,
			["characterKey"] = session.CharacterKey,
			["characterCreationTime"] = session.CharacterCreationTime
		};
		await ExecuteAsync(
			"UPSERT type::thing('session', $token) SET accountId = $accountId, expiryUnixMs = $expiryUnixMs, ttlMs = $ttlMs, originIp = $originIp, characterKey = $characterKey, characterCreationTime = $characterCreationTime",
			parameters, cancellationToken);
	}

	/// <summary>
	/// UPDATE, never UPSERT: since SurrealDB 2.0 the two are distinct statements, and UPDATE on a record
	/// that does not exist affects nothing and creates nothing. The embedded engine serialises the
	/// statement itself, so no extra guard is needed against a concurrent revoke.
	/// </summary>
	public async ValueTask<bool> TouchSessionExpiryAsync(string token, long expiryUnixMs,
		CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync(
			"UPDATE type::thing('session', $token) SET expiryUnixMs = $expiryUnixMs RETURN AFTER",
			new Dictionary<string, object?> { ["token"] = token, ["expiryUnixMs"] = expiryUnixMs },
			cancellationToken);
		return response.GetValue<List<SessionDbRecord>>(0) is { Count: > 0 };
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
			OriginIp = row.originIp,
			CharacterKey = row.characterKey,
			CharacterCreationTime = row.characterCreationTime
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

	/// <inheritdoc />
	public async ValueTask<string[]> GetSessionOriginIpsAsync(CancellationToken cancellationToken = default)
	{
		var response = await ExecuteAsync("SELECT VALUE originIp FROM session",
			new Dictionary<string, object?>(), cancellationToken);
		var results = response.GetValue<List<string?>>(0) ?? [];
		return
		[
			.. results
				.Where(ip => !string.IsNullOrEmpty(ip))
				.Select(ip => ip!)
				.Distinct(StringComparer.OrdinalIgnoreCase)
		];
	}

	#endregion
}
