using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="ISessionRecordStore"/>: persisted account-session records keyed by token. Ported from
/// <c>SurrealDatabase.Sessions.cs</c>. <see cref="Tables.SessionAccount"/> and <see cref="Tables.SessionIp"/>
/// are secondary duplicate indexes (accountId/originIp → token) kept in step with <see cref="Tables.Session"/>
/// inside the same writer job as every mutation that touches them.
/// </summary>
public sealed partial class LightningDatabase
{
	private static SharpSession MapToSharpSession(string token, SessionRecord record) => new()
	{
		Token = token,
		AccountId = record.AccountId,
		ExpiryUnixMs = record.ExpiryUnixMs,
		TtlMs = record.TtlMs,
		OriginIp = record.OriginIp,
		CharacterKey = record.CharacterKey,
		CharacterCreationTime = record.CharacterCreationTime
	};

	public async ValueTask UpsertSessionAsync(SharpSession session, CancellationToken cancellationToken = default)
	{
		var key = Keys.Str(session.Token);
		await Store.WriteAsync(tx =>
		{
			// Replacing an existing session: drop its old secondary entries first so a token that
			// changes account or origin IP does not leave a stale token behind under the old key.
			if (tx.TryGet(Tables.Session, key, out var existingBytes))
			{
				var existing = Codec.Deserialize<SessionRecord>(existingBytes);
				tx.Delete(Tables.SessionAccount, Keys.Str(existing.AccountId), key);
				tx.Delete(Tables.SessionIp, Keys.Str(existing.OriginIp), key);
			}

			var record = new SessionRecord
			{
				AccountId = session.AccountId,
				ExpiryUnixMs = session.ExpiryUnixMs,
				TtlMs = session.TtlMs,
				OriginIp = session.OriginIp,
				CharacterKey = session.CharacterKey,
				CharacterCreationTime = session.CharacterCreationTime
			};
			tx.Put(Tables.Session, key, Codec.Serialize(record));
			tx.Put(Tables.SessionAccount, Keys.Str(session.AccountId), key);
			tx.Put(Tables.SessionIp, Keys.Str(session.OriginIp), key);
		}, cancellationToken);
	}

	public ValueTask<SharpSession?> GetSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => tx.TryGet(Tables.Session, Keys.Str(token), out var bytes)
			? MapToSharpSession(token, Codec.Deserialize<SessionRecord>(bytes))
			: null);
		return ValueTask.FromResult(result);
	}

	/// <summary>
	/// A conditional update: no-ops (returns <see langword="false"/>, writes nothing) when the token is
	/// absent, because a renewal racing a revocation must never reinstate a session the revocation just
	/// deleted. See the contract's remarks on <c>ISessionRecordStore.TouchSessionExpiryAsync</c>.
	/// </summary>
	public async ValueTask<bool> TouchSessionExpiryAsync(string token, long expiryUnixMs, CancellationToken cancellationToken = default)
	{
		var key = Keys.Str(token);
		return await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Session, key, out var bytes))
				return false;

			var record = Codec.Deserialize<SessionRecord>(bytes);
			tx.Put(Tables.Session, key, Codec.Serialize(record with { ExpiryUnixMs = expiryUnixMs }));
			return true;
		}, cancellationToken);
	}

	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var key = Keys.Str(token);
		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Session, key, out var bytes)) return;
			var record = Codec.Deserialize<SessionRecord>(bytes);
			tx.Delete(Tables.Session, key);
			tx.Delete(Tables.SessionAccount, Keys.Str(record.AccountId), key);
			tx.Delete(Tables.SessionIp, Keys.Str(record.OriginIp), key);
		}, cancellationToken);
	}

	public async ValueTask DeleteSessionsForAccountAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var acctKey = Keys.Str(accountId);
		await Store.WriteAsync(tx =>
		{
			foreach (var token in tx.Dups(Tables.SessionAccount, acctKey).ToList())
			{
				if (tx.TryGet(Tables.Session, token, out var bytes))
				{
					var record = Codec.Deserialize<SessionRecord>(bytes);
					tx.Delete(Tables.SessionIp, Keys.Str(record.OriginIp), token);
					tx.Delete(Tables.Session, token);
				}
				tx.Delete(Tables.SessionAccount, acctKey, token);
			}
		}, cancellationToken);
	}

	public async ValueTask DeleteSessionsForIpAsync(string originIp, CancellationToken cancellationToken = default)
	{
		var ipKey = Keys.Str(originIp);
		await Store.WriteAsync(tx =>
		{
			foreach (var token in tx.Dups(Tables.SessionIp, ipKey).ToList())
			{
				if (tx.TryGet(Tables.Session, token, out var bytes))
				{
					var record = Codec.Deserialize<SessionRecord>(bytes);
					tx.Delete(Tables.SessionAccount, Keys.Str(record.AccountId), token);
					tx.Delete(Tables.Session, token);
				}
				tx.Delete(Tables.SessionIp, ipKey, token);
			}
		}, cancellationToken);
	}

	public ValueTask<string[]> GetSessionOriginIpsAsync(CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => tx.Range(Tables.SessionIp, [])
			.Select(e => Keys.ReadStr(e.Key))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray());
		return ValueTask.FromResult(result);
	}
}
