using System.Text.Json;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
	{
		await arangoDb.Query.ExecuteAsync<object>(handle,
			"UPSERT { _key: @key } INSERT @doc REPLACE @doc IN @@c",
			bindVars: new Dictionary<string, object>
			{
				{ "@c", DatabaseConstants.Sessions },
				{ "key", session.Token },
				{ "doc", new Dictionary<string, object?>
					{
						["_key"] = session.Token,
						["AccountId"] = session.AccountId,
						["ExpiryUnixMs"] = session.ExpiryUnixMs,
						["TtlMs"] = session.TtlMs,
						["OriginIp"] = session.OriginIp
					} }
			}, cancellationToken: cancellationToken);
	}

	public async ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<JsonElement>(handle,
			"FOR d IN @@c FILTER d._key == @key RETURN d",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "key", token } },
			cancellationToken: cancellationToken);
		if (result.FirstOrDefault() is not { ValueKind: JsonValueKind.Object } e) return null;
		return new SharpSession
		{
			Token = token,
			AccountId = e.GetProperty("AccountId").GetString()!,
			ExpiryUnixMs = e.GetProperty("ExpiryUnixMs").GetInt64(),
			TtlMs = e.GetProperty("TtlMs").GetInt64(),
			OriginIp = e.GetProperty("OriginIp").GetString()!
		};
	}

	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
		=> await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d._key == @key REMOVE d IN @@c",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "key", token } },
			cancellationToken: cancellationToken);

	public async ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d.AccountId == @a REMOVE d IN @@c",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "a", accountId } },
			cancellationToken: cancellationToken);

	public async ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
		=> await arangoDb.Query.ExecuteAsync<object>(handle,
			"FOR d IN @@c FILTER d.OriginIp == @ip REMOVE d IN @@c",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "ip", originIp } },
			cancellationToken: cancellationToken);
}
