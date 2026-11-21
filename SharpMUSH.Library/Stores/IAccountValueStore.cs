using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Account values: named JSON kept on a web account, each with an owner and a lock. Names and JSON are already checked
/// by <see cref="AccountValues"/>. A writer is the objid of the player a change is made for; with
/// <c>overrideLock</c> a locked value changes whoever owns it. Every lock check is made inside the write.
/// </summary>
public interface IAccountValueStore
{
	/// <summary>The account's value <paramref name="name"/>, or <see cref="NotFound"/>.</summary>
	ValueTask<Found<AccountValue>> GetAccountValueAsync(string accountId, string name, CancellationToken cancellationToken = default);

	/// <summary>The account's values in ordinal order of name. Empty for an account with none, or no such account.</summary>
	ValueTask<IReadOnlyList<AccountValue>> GetAccountValuesAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Keeps <paramref name="json"/> under <paramref name="name"/>. A new value is owned by <paramref name="writer"/>
	/// and unlocked, and is refused once the account holds <paramref name="maxPerAccount"/>; an existing one keeps its
	/// owner and lock.
	/// </summary>
	ValueTask<AccountValueWrite> SetAccountValueAsync(string accountId, string name, string json, string writer, bool overrideLock,
		int maxPerAccount, CancellationToken cancellationToken = default);

	/// <summary>Removes the account's value <paramref name="name"/>.</summary>
	ValueTask<AccountValueChange> RemoveAccountValueAsync(string accountId, string name, string writer, bool overrideLock,
		CancellationToken cancellationToken = default);

	/// <summary>Locks or unlocks the value. Locking also makes <paramref name="writer"/> its owner, as <c>@atrlock</c> does.</summary>
	ValueTask<AccountValueChange> SetAccountValueLockAsync(string accountId, string name, string writer, bool locked, bool overrideLock,
		CancellationToken cancellationToken = default);
}
