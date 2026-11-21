using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAccountValueStore"/> in <see cref="Tables.AccountValue"/>, keyed account key + 0x00 + name, each row an
/// <see cref="AccountValueRecord"/>, and the rules in <see cref="Tables.AccountValueRule"/>. Each write compares the locks
/// its caller saw inside the write that acts on them. Accounts are never removed, so nothing cascades.
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

	public ValueTask<AccountValueWrite> SetAccountValueAsync(string accountId, string name, string json,
		Found<AccountValueLocks> expected, int maxPerAccount, CancellationToken cancellationToken = default)
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
				if (expected is not AccountValueLocks seen || seen != LocksOf(existing)) return new AccountValueChanged();
				tx.Put(Tables.AccountValue, valueKey, Codec.Serialize(existing with { Json = json }));
				return new Success();
			}

			if (expected is not NotFound) return new AccountValueChanged();
			if (tx.Range(Tables.AccountValue, prefix).Take(maxPerAccount).Count() >= maxPerAccount)
				return new AccountValuesFull();

			tx.Put(Tables.AccountValue, valueKey, Codec.Serialize(new AccountValueRecord { Json = json }));
			return new Success();
		}, cancellationToken);
	}

	public ValueTask<AccountValueChange> RemoveAccountValueAsync(string accountId, string name, AccountValueLocks expected,
		CancellationToken cancellationToken = default)
	{
		var valueKey = Keys.Composite(ParseAccountId(accountId), name);
		return Store.WriteAsync<AccountValueChange>(tx =>
		{
			if (!tx.TryGet(Tables.AccountValue, valueKey, out var bytes)) return new NotFound();
			if (LocksOf(Codec.Deserialize<AccountValueRecord>(bytes)) != expected) return new AccountValueChanged();
			tx.Delete(Tables.AccountValue, valueKey);
			return new Success();
		}, cancellationToken);
	}

	public ValueTask<AccountValueChange> SetAccountValueLocksAsync(string accountId, string name, AccountValueLocks expected,
		AccountValueLocks locks, CancellationToken cancellationToken = default)
	{
		var valueKey = Keys.Composite(ParseAccountId(accountId), name);
		return Store.WriteAsync<AccountValueChange>(tx =>
		{
			if (!tx.TryGet(Tables.AccountValue, valueKey, out var bytes)) return new NotFound();
			var existing = Codec.Deserialize<AccountValueRecord>(bytes);
			if (LocksOf(existing) != expected) return new AccountValueChanged();
			tx.Put(Tables.AccountValue, valueKey,
				Codec.Serialize(existing with { View = locks.View, Write = locks.Write, Delete = locks.Delete }));
			return new Success();
		}, cancellationToken);
	}

	public ValueTask<IReadOnlyList<AccountValueRule>> GetAccountValueRulesAsync(CancellationToken cancellationToken = default)
	{
		IReadOnlyList<AccountValueRule> rules = Store.Read(tx => tx.Range(Tables.AccountValueRule, [])
			.Select(entry =>
			{
				var record = Codec.Deserialize<AccountValueRuleRecord>(entry.Value);
				return new AccountValueRule(Keys.ReadStr(entry.Key), new AccountValueLocks(record.View, record.Write, record.Delete));
			})
			.ToList());
		return ValueTask.FromResult(rules);
	}

	public async ValueTask SetAccountValueRuleAsync(string pattern, AccountValueLocks locks, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (locks.IsEmpty) tx.Delete(Tables.AccountValueRule, Keys.Str(pattern));
			else
				tx.Put(Tables.AccountValueRule, Keys.Str(pattern),
					Codec.Serialize(new AccountValueRuleRecord { View = locks.View, Write = locks.Write, Delete = locks.Delete }));
			return true;
		}, cancellationToken);

	private static AccountValueLocks LocksOf(AccountValueRecord record) => new(record.View, record.Write, record.Delete);

	private static AccountValue MapToAccountValue(string name, AccountValueRecord record)
		=> new(name, record.Json, LocksOf(record));

	private static byte[] AccountValuePrefix(string accountId) => Keys.Composite(ParseAccountId(accountId), "");
}
