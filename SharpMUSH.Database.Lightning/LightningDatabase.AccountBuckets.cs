using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IAccountBucketStore"/> in <see cref="Tables.AccountBucket"/>, keyed account key + 0x00 + name. The
/// value is the JSON's UTF-8 bytes as given, so a read decodes text and never fails on a stored row. Accounts are
/// never removed, so nothing cascades.
/// </summary>
public partial class LightningDatabase : IAccountBucketStore
{
	public ValueTask<Found<string>> GetAccountBucketAsync(string accountId, string name, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<string>>(tx =>
			tx.TryGet(Tables.AccountBucket, Keys.Composite(ParseAccountId(accountId), name), out var value)
				? Keys.ReadStr(value)
				: new NotFound()));

	public ValueTask<IReadOnlyList<string>> GetAccountBucketNamesAsync(string accountId, CancellationToken cancellationToken = default)
	{
		var prefix = BucketPrefix(accountId);
		IReadOnlyList<string> names = Store.Read(tx => tx.Range(Tables.AccountBucket, prefix)
			.Select(entry => Keys.ReadStr(entry.Key.AsSpan(prefix.Length)))
			.ToList());
		return ValueTask.FromResult(names);
	}

	public ValueTask<AccountBucketWrite> SetAccountBucketAsync(string accountId, string name, string json, int maxPerAccount,
		CancellationToken cancellationToken = default)
	{
		var key = ParseAccountId(accountId);
		var bucketKey = Keys.Composite(key, name);
		var prefix = BucketPrefix(accountId);
		return Store.WriteAsync<AccountBucketWrite>(tx =>
		{
			if (!tx.TryGet(Tables.Account, Keys.Str(key), out _)) return new NotFound();
			if (!tx.TryGet(Tables.AccountBucket, bucketKey, out _)
				&& tx.Range(Tables.AccountBucket, prefix).Take(maxPerAccount).Count() >= maxPerAccount)
			{
				return new AccountBucketsFull();
			}

			tx.Put(Tables.AccountBucket, bucketKey, Keys.Str(json));
			return new Success();
		}, cancellationToken);
	}

	public ValueTask<bool> RemoveAccountBucketAsync(string accountId, string name, CancellationToken cancellationToken = default)
	{
		var bucketKey = Keys.Composite(ParseAccountId(accountId), name);
		return Store.WriteAsync(tx => tx.Delete(Tables.AccountBucket, bucketKey), cancellationToken);
	}

	private static byte[] BucketPrefix(string accountId) => Keys.Composite(ParseAccountId(accountId), "");
}
