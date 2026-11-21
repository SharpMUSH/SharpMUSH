using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAccountValueStore"/> in <see cref="Tables.AccountValue"/>, keyed account key + 0x00 + name, each row an
/// <see cref="AccountValueRecord"/>. Every owner and lock check is made inside the write that acts on it. Accounts are
/// never removed, so nothing cascades.
/// </summary>
public partial class LightningDatabase : IAccountValueStore
{
	public ValueTask<Found<AccountValue>> GetAccountValueAsync(string accountId, string name, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<AccountValue>>(tx =>
			tx.TryGet(Tables.AccountValue, Keys.Composite(ParseAccountId(accountId), name), out var bytes)
				? MapToAccountValue(name, Codec.Deserialize<AccountValueRecord>(bytes))
				: new NotFound()));

	public ValueTask<IReadOnlyList<AccountValue>> GetAccountValuesAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var prefix = AccountValuePrefix(accountId);
		IReadOnlyList<AccountValue> values = Store.Read(tx => tx.Range(Tables.AccountValue, prefix)
			.Select(entry => MapToAccountValue(Keys.ReadStr(entry.Key.AsSpan(prefix.Length)),
				Codec.Deserialize<AccountValueRecord>(entry.Value)))
			.ToList());
		return ValueTask.FromResult(values);
	}

	public ValueTask<AccountValueWrite> SetAccountValueAsync(string accountId, string name, string json, string writer,
		bool overrideLock, int maxPerAccount, CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		var valueKey = Keys.Composite(key, name);
		var prefix = AccountValuePrefix(accountId);
		return Store.WriteAsync<AccountValueWrite>(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(key), out _)) return new NotFound();
			if (tx.TryGet(Tables.AccountValue, valueKey, out var bytes))
			{
				var existing = Codec.Deserialize<AccountValueRecord>(bytes);
				if (!MayChange(existing, writer, overrideLock)) return new AccountValueLocked();
				tx.Put(Tables.AccountValue, valueKey, Codec.Serialize(existing with { Json = json }));
				return new Success();
			}

			if (tx.Range(Tables.AccountValue, prefix).Take(maxPerAccount).Count() >= maxPerAccount)
				return new AccountValuesFull();

			tx.Put(Tables.AccountValue, valueKey, Codec.Serialize(new AccountValueRecord { Json = json, Owner = writer }));
			return new Success();
		}, cancellationToken);
	}

	public ValueTask<AccountValueChange> RemoveAccountValueAsync(string accountId, string name, string writer, bool overrideLock,
		CancellationToken cancellationToken = default)
	{
		var valueKey = Keys.Composite(ParseAccountId(accountId), name);
		return Store.WriteAsync<AccountValueChange>(tx =>
		{
			if (!tx.TryGet(Tables.AccountValue, valueKey, out var bytes)) return new NotFound();
			if (!MayChange(Codec.Deserialize<AccountValueRecord>(bytes), writer, overrideLock)) return new AccountValueLocked();
			tx.Delete(Tables.AccountValue, valueKey);
			return new Success();
		}, cancellationToken);
	}

	public ValueTask<AccountValueChange> SetAccountValueLockAsync(string accountId, string name, string writer, bool locked,
		bool overrideLock, CancellationToken cancellationToken = default)
	{
		var valueKey = Keys.Composite(ParseAccountId(accountId), name);
		return Store.WriteAsync<AccountValueChange>(tx =>
		{
			if (!tx.TryGet(Tables.AccountValue, valueKey, out var bytes)) return new NotFound();
			var existing = Codec.Deserialize<AccountValueRecord>(bytes);
			if (!MayChange(existing, writer, overrideLock)) return new AccountValueLocked();
			var changed = locked ? existing with { Locked = true, Owner = writer } : existing with { Locked = false };
			tx.Put(Tables.AccountValue, valueKey, Codec.Serialize(changed));
			return new Success();
		}, cancellationToken);
	}

	/// <summary>An unlocked value changes for anyone the caller let through; a locked one for its owner or an override.</summary>
	private static bool MayChange(AccountValueRecord record, string writer, bool overrideLock)
		=> !record.Locked || overrideLock || string.Equals(record.Owner, writer, StringComparison.Ordinal);

	private static AccountValue MapToAccountValue(string name, AccountValueRecord record)
		=> new(name, record.Json, record.Owner, record.Locked);

	private static byte[] AccountValuePrefix(string accountId) => Keys.Composite(ParseAccountId(accountId), "");
}
