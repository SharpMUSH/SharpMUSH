using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Account buckets: named JSON values kept on a web account, for softcode to read and write. A bucket's
/// name is already checked and folded by <see cref="AccountBuckets.Name"/>, and its value checked by
/// <see cref="AccountBuckets.Json"/>; the store keeps both as given.
/// </summary>
public interface IAccountBucketStore
{
	/// <summary>The JSON kept in the account's bucket <paramref name="name"/>, or <see cref="NotFound"/> when it has none.</summary>
	ValueTask<Found<string>> GetAccountBucketAsync(string accountId, string name, CancellationToken cancellationToken = default);

	/// <summary>The names of the account's buckets, in ordinal order. Empty for an account with none, or no such account.</summary>
	ValueTask<IReadOnlyList<string>> GetAccountBucketNamesAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Keeps <paramref name="json"/> in the account's bucket <paramref name="name"/>, replacing what was there.
	/// A new bucket is refused once the account holds <paramref name="maxPerAccount"/>; that and whether the
	/// account exists are decided inside the write.
	/// </summary>
	ValueTask<AccountBucketWrite> SetAccountBucketAsync(string accountId, string name, string json, int maxPerAccount,
		CancellationToken cancellationToken = default);

	/// <summary>Removes the account's bucket <paramref name="name"/>. False when there was none.</summary>
	ValueTask<bool> RemoveAccountBucketAsync(string accountId, string name, CancellationToken cancellationToken = default);
}
