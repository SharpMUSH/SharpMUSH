using System.Text.Json;
using Core.Arango;
using Core.Arango.Protocol;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Database.ArangoDB;

public partial class ArangoDatabase
{
	/// <summary>
	/// Runs a session mutation inside an exclusive transaction on the sessions collection. A bare AQL
	/// UPSERT lets two concurrent writers touch the same session document and lose the race with a 409
	/// ("write-write conflict" / "timeout waiting to lock key") that surfaces to the caller as a 500 —
	/// and concurrent writers are the norm here, because every authenticated request slides the same
	/// session's rolling expiry. Same forced-serialisation pattern as the channel and object writes.
	/// </summary>
	private async ValueTask InTransactionAsync(Func<ArangoHandle, ValueTask> work, CancellationToken cancellationToken)
	{
		var transaction = await arangoDb.Transaction.BeginAsync(handle,
			new ArangoTransaction
			{
				Collections = new ArangoTransactionScope { Exclusive = [DatabaseConstants.Sessions] }
			}, cancellationToken);

		try
		{
			await work(transaction);
			await arangoDb.Transaction.CommitAsync(transaction, cancellationToken);
		}
		catch
		{
			await arangoDb.Transaction.AbortAsync(transaction, cancellationToken);
			throw;
		}
	}

	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
		=> await InTransactionAsync(async tx =>
			await arangoDb.Query.ExecuteAsync<object>(tx,
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
							["OriginIp"] = session.OriginIp,
							["CharacterKey"] = session.CharacterKey,
							["CharacterCreationTime"] = session.CharacterCreationTime
						} }
				}, cancellationToken: cancellationToken), cancellationToken);

	/// <summary>
	/// UPDATE, never UPSERT: the FOR/FILTER finds nothing when the document is gone, so the whole statement
	/// is a no-op rather than an insert. Same exclusive transaction as the other session writes, so this
	/// cannot interleave with a concurrent revoke either — it either runs before the delete and is undone
	/// by it, or after and finds nothing.
	/// </summary>
	public async ValueTask<bool> TouchSessionExpiryAsync(string token, long expiryUnixMs,
		CancellationToken cancellationToken = default)
	{
		var touched = false;
		await InTransactionAsync(async tx =>
		{
			var updated = await arangoDb.Query.ExecuteAsync<string>(tx,
				"FOR d IN @@c FILTER d._key == @key UPDATE d WITH { ExpiryUnixMs: @expiry } IN @@c RETURN NEW._key",
				bindVars: new Dictionary<string, object>
				{
					{ "@c", DatabaseConstants.Sessions }, { "key", token }, { "expiry", expiryUnixMs }
				}, cancellationToken: cancellationToken);
			touched = updated.Count > 0;
		}, cancellationToken);
		return touched;
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
			OriginIp = e.GetProperty("OriginIp").GetString()!,
			// Sessions minted before the character binding existed simply have no property.
			CharacterKey = e.TryGetProperty("CharacterKey", out var ck) && ck.ValueKind == JsonValueKind.Number
				? ck.GetInt32() : null,
			CharacterCreationTime = e.TryGetProperty("CharacterCreationTime", out var cc) && cc.ValueKind == JsonValueKind.Number
				? cc.GetInt64() : null
		};
	}

	// The deletes take the same exclusive transaction, because they contend for the same documents the
	// upsert does. Expiring one token is reached from the authentication path (a burst of requests carrying
	// one stale bearer all try to remove the same document), and a ban revoking an account's or an IP's
	// sessions removes documents that live requests may be sliding at that instant.
	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
		=> await InTransactionAsync(async tx =>
			await arangoDb.Query.ExecuteAsync<object>(tx,
				"FOR d IN @@c FILTER d._key == @key REMOVE d IN @@c",
				bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "key", token } },
				cancellationToken: cancellationToken), cancellationToken);

	public async ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
		=> await InTransactionAsync(async tx =>
			await arangoDb.Query.ExecuteAsync<object>(tx,
				"FOR d IN @@c FILTER d.AccountId == @a REMOVE d IN @@c",
				bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "a", accountId } },
				cancellationToken: cancellationToken), cancellationToken);

	public async ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
		=> await InTransactionAsync(async tx =>
			await arangoDb.Query.ExecuteAsync<object>(tx,
				"FOR d IN @@c FILTER d.OriginIp == @ip REMOVE d IN @@c",
				bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions }, { "ip", originIp } },
				cancellationToken: cancellationToken), cancellationToken);

	// Read, so no transaction — same reasoning as GetSessionAsync. Ban enforcement runs off the
	// admin path, not the request path, and the answer only has to be as current as the moment the
	// ban landed: a session created after this returns is one the validation-time sitelock check
	// refuses anyway.
	public async ValueTask<string[]> GetSessionOriginIpsAsync(CancellationToken cancellationToken = default)
	{
		var result = await arangoDb.Query.ExecuteAsync<string>(handle,
			"FOR d IN @@c RETURN DISTINCT d.OriginIp",
			bindVars: new Dictionary<string, object> { { "@c", DatabaseConstants.Sessions } },
			cancellationToken: cancellationToken);
		return [.. result.Where(ip => !string.IsNullOrEmpty(ip))];
	}
}
