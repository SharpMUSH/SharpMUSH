namespace SharpMUSH.Library.Models;

/// <summary>
/// The single lifecycle state of a <see cref="SharpAccount"/>. Account documents are never
/// deleted, so historical references to an account always resolve; <see cref="Closed"/> and
/// <see cref="Deleted"/> are terminal-by-intent rather than removals, and every non-<see cref="Active"/>
/// state is admin-reversible.
/// </summary>
public enum AccountStatus
{
	Active,
	Disabled,
	Closed,
	Deleted
}

public static class AccountStatusParser
{
	/// <summary>
	/// Reads a persisted status. A <see langword="null"/> or empty value means the field was never
	/// written, which is an active account. Any other unrecognised value fails closed to
	/// <see cref="AccountStatus.Disabled"/>: <see cref="SharpAccount.Status"/> gates authentication,
	/// and a corrupt value must not be able to re-enable a closed account.
	/// </summary>
	public static AccountStatus Parse(string? stored)
		=> string.IsNullOrEmpty(stored)
			? AccountStatus.Active
			: Enum.TryParse<AccountStatus>(stored, out var parsed)
				? parsed
				: AccountStatus.Disabled;
}
