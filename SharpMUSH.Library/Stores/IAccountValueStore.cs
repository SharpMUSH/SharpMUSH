using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Account values: named JSON kept on a web account, each with its own view, write and delete locks, and the game-wide
/// rules (<see cref="AccountValueRule"/>) that lock values by name. Names, patterns, JSON and locks are already checked.
/// Locks are evaluated before a write, so each write names the locks its caller saw (<c>expected</c>: the value's
/// locks, or <see cref="NotFound"/> when there was no value) and is refused with <see cref="AccountValueChanged"/> if
/// they are no longer what it holds.
/// </summary>
public interface IAccountValueStore
{
	/// <summary>The account's value <paramref name="name"/>, or <see cref="NotFound"/>.</summary>
	ValueTask<Found<AccountValue>> GetAccountValueAsync(string accountId, string name, CancellationToken cancellationToken = default);

	/// <summary>The account's values in ordinal order of name. Empty for an account with none, or no such account.</summary>
	ValueTask<IReadOnlyList<AccountValue>> GetAccountValuesAsync(string accountId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Keeps <paramref name="json"/> under <paramref name="name"/>. A new value has no locks of its own and is refused
	/// once the account holds <paramref name="maxPerAccount"/>; an existing one keeps its locks.
	/// </summary>
	ValueTask<AccountValueWrite> SetAccountValueAsync(string accountId, string name, string json, Found<AccountValueLocks> expected,
		int maxPerAccount, CancellationToken cancellationToken = default);

	/// <summary>Removes the account's value <paramref name="name"/>.</summary>
	ValueTask<AccountValueChange> RemoveAccountValueAsync(string accountId, string name, AccountValueLocks expected,
		CancellationToken cancellationToken = default);

	/// <summary>Replaces the value's own locks with <paramref name="locks"/>.</summary>
	ValueTask<AccountValueChange> SetAccountValueLocksAsync(string accountId, string name, AccountValueLocks expected,
		AccountValueLocks locks, CancellationToken cancellationToken = default);

	/// <summary>Every rule, in ordinal order of pattern.</summary>
	ValueTask<IReadOnlyList<AccountValueRule>> GetAccountValueRulesAsync(CancellationToken cancellationToken = default);

	/// <summary>Keeps the rule for <paramref name="pattern"/>, or removes it when <paramref name="locks"/> sets nothing.</summary>
	ValueTask SetAccountValueRuleAsync(string pattern, AccountValueLocks locks, CancellationToken cancellationToken = default);
}
