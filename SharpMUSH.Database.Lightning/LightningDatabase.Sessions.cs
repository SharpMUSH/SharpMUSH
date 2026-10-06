using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="ISessionRecordStore"/>: persisted account-session records keyed by token.
/// <see cref="Tables.SessionAccount"/> and <see cref="Tables.SessionIp"/>
/// are secondary duplicate indexes (accountId/originIp → token) kept in step with <see cref="Tables.Session"/>
/// inside the same writer job as every mutation that touches them. <see cref="Tables.SessionExpiry"/> orders
/// the sessions by expiry (expiry millis + token → empty) so the sweep reads only the expired head; it moves
/// in the same job as every create, renewal and delete, so a renewal and a sweep never disagree about a
/// session's expiry.
/// </summary>
public partial class LightningDatabase
{
	private static byte[] SessionExpiryKey(long expiryUnixMs, byte[] token) => Keys.Concat(Keys.Dbref(expiryUnixMs), token);

	/// <summary>Removes a session row and every index entry pointing at it, inside the caller's write job.</summary>
	private static void DeleteSessionRow(ITx tx, byte[] token, SessionRecord record)
	{
		tx.Delete(Tables.Session, token);
		tx.Delete(Tables.SessionAccount, Keys.Str(record.AccountId), token);
		tx.Delete(Tables.SessionIp, Keys.Str(record.OriginIp), token);
		tx.Delete(Tables.SessionExpiry, SessionExpiryKey(record.ExpiryUnixMs, token));
	}

	private static SharpSession MapToSharpSession(string token, SessionRecord record) => new()
	{
		Token = token,
		AccountId = record.AccountId,
		ExpiryUnixMs = record.ExpiryUnixMs,
		TtlMs = record.TtlMs,
		OriginIp = record.OriginIp,
		CharacterKey = record.CharacterKey,
		CharacterCreationTime = record.CharacterCreationTime,
		Remembered = record.Remembered
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
				tx.Delete(Tables.SessionExpiry, SessionExpiryKey(existing.ExpiryUnixMs, key));
			}

			var record = new SessionRecord
			{
				AccountId = session.AccountId,
				ExpiryUnixMs = session.ExpiryUnixMs,
				TtlMs = session.TtlMs,
				OriginIp = session.OriginIp,
				CharacterKey = session.CharacterKey,
				CharacterCreationTime = session.CharacterCreationTime,
				Remembered = session.Remembered
			};
			tx.Put(Tables.Session, key, Codec.Serialize(record));
			tx.Put(Tables.SessionAccount, Keys.Str(session.AccountId), key);
			tx.Put(Tables.SessionIp, Keys.Str(session.OriginIp), key);
			tx.Put(Tables.SessionExpiry, SessionExpiryKey(session.ExpiryUnixMs, key), []);
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
			tx.Delete(Tables.SessionExpiry, SessionExpiryKey(record.ExpiryUnixMs, key));
			tx.Put(Tables.SessionExpiry, SessionExpiryKey(expiryUnixMs, key), []);
			return true;
		}, cancellationToken);
	}

	public async ValueTask DeleteSessionAsync(string token, CancellationToken cancellationToken = default)
	{
		var key = Keys.Str(token);
		await Store.WriteAsync(tx =>
		{
			if (!tx.TryGet(Tables.Session, key, out var bytes)) return;
			DeleteSessionRow(tx, key, Codec.Deserialize<SessionRecord>(bytes));
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
					DeleteSessionRow(tx, token, Codec.Deserialize<SessionRecord>(bytes));
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
					DeleteSessionRow(tx, token, Codec.Deserialize<SessionRecord>(bytes));
				}
				tx.Delete(Tables.SessionIp, ipKey, token);
			}
		}, cancellationToken);
	}

	/// <summary>
	/// One seek per distinct address rather than one row per session: after reading a key, the cursor
	/// jumps to the smallest key past it (the key with a 0x00 appended), so the duplicate run behind an
	/// address shared by many sessions is never walked.
	/// </summary>
	public ValueTask<string[]> GetSessionOriginIpsAsync(CancellationToken cancellationToken = default)
	{
		var result = Store.Read(tx => DistinctKeys(tx, Tables.SessionIp)
			.Select(key => Keys.ReadStr(key))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray());
		return ValueTask.FromResult(result);
	}

	/// <summary>Every distinct key of <paramref name="table"/> in key order, one cursor seek per key.</summary>
	internal static IEnumerable<byte[]> DistinctKeys(ITx tx, TableDef table)
	{
		var next = tx.Range(table, []).Select(e => e.Key).FirstOrDefault();
		while (next is not null)
		{
			yield return next;
			next = tx.RangeFromKey(table, Keys.Concat(next, Keys.Sep)).Select(e => e.Key).FirstOrDefault();
		}
	}

	public async ValueTask<int> DeleteExpiredSessionsAsync(long nowUnixMs, int maxCount, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			// The head of the expiry index, materialised before anything is deleted from it. Read and
			// delete share one write job, so a renewal either landed before it (and moved the session's
			// entry past now) or lands after it (and finds the session gone, which fails the renewal).
			var due = tx.Range(Tables.SessionExpiry, [])
				.TakeWhile(e => Keys.ReadDbref(e.Key) <= nowUnixMs)
				.Take(maxCount)
				.Select(e => e.Key)
				.ToList();

			var deleted = 0;
			foreach (var entry in due)
			{
				var token = entry[8..];
				tx.Delete(Tables.SessionExpiry, entry);
				if (!tx.TryGet(Tables.Session, token, out var bytes)) continue;
				var record = Codec.Deserialize<SessionRecord>(bytes);
				// The row is the authority; an index entry that disagrees with it is a leftover, not a verdict.
				if (record.ExpiryUnixMs > nowUnixMs) continue;
				DeleteSessionRow(tx, token, record);
				deleted++;
			}

			return deleted;
		}, cancellationToken);

	public ValueTask<long> CountExpiredSessionsAsync(long nowUnixMs, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read(tx => tx.Range(Tables.SessionExpiry, [])
			.TakeWhile(e => Keys.ReadDbref(e.Key) <= nowUnixMs)
			.LongCount()));
}
