using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

public partial class LightningDatabase
{
	internal const string AccountBucketMigrationId = "0012_account_buckets_to_attributes";

	/// <summary>A bucket name that is also a stored attribute name: no tree separator, wildcard, colon or parenthesis.</summary>
	private static readonly Regex MovableBucketName = new(@"\A[A-Z0-9_\-.!#$&'+,;<=>@^|~]+\z", RegexOptions.CultureInvariant);

	/// <summary>
	/// Moves each account bucket a development build wrote (<see cref="Tables.AccountBucket"/>) onto its account's holder
	/// (<see cref="AccountHolders"/>) as an attribute named for the bucket, upper-cased, owned by #1, holding the JSON as
	/// plain text. A bucket whose account is gone, whose name is not an attribute name, or whose holder already has that
	/// attribute is left where it is, so nothing is overwritten or lost; the rest are removed once moved.
	/// </summary>
	private void MigrateAccountBuckets(ITx tx)
	{
		var marker = Keys.Str("mig:" + AccountBucketMigrationId);
		if (tx.TryGet(Tables.Meta, marker, out _)) return;

		var kept = 0;
		foreach (var (key, value) in tx.Range(Tables.AccountBucket, []).ToList())
		{
			var separator = Array.IndexOf(key, (byte)0);
			var accountKey = separator > 0 ? Keys.ReadStr(key.AsSpan(0, separator)) : null;
			var name = separator > 0 ? Keys.ReadStr(key.AsSpan(separator + 1)).ToUpperInvariant() : null;
			if (AccountHolders.ForAccount(accountKey) is not { } holder || name is null || !MovableBucketName.IsMatch(name)
				|| !tx.TryGet(Tables.Account, Keys.Str(accountKey!), out _)
				|| tx.TryGet(Tables.AttrMeta, Keys.Attr(holder.Number, name), out _))
			{
				kept++;
				continue;
			}

			var json = MarkupText.Plain(Keys.ReadStr(value));
			var write = new PreparedAttributeWrite([name], Keys.Str(MarkupTextSerializer.Serialize(json)), 1, [], json);
			WriteAttributePath(tx, holder.Number, DatabaseConstants.TypeThing, write, WatchedAttributes.None.Names, out _);
			tx.Delete(Tables.AccountBucket, key);
		}

		if (kept > 0)
			_logger.LogWarning("{Count} account bucket(s) could not become account attributes and stay in acct.bucket.", kept);

		tx.Put(Tables.Meta, marker, Codec.Serialize(new MigrationRecord
		{
			Id = AccountBucketMigrationId, AppliedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
		}));
	}
}
