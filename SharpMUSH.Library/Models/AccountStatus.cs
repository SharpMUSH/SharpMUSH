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
		=> string.IsNullOrWhiteSpace(stored)
			? AccountStatus.Active
			: TryParseName(stored, out var parsed)
				? parsed
				: AccountStatus.Disabled;

	/// <summary>
	/// Matches a status <em>name</em>, case-insensitively and ignoring surrounding whitespace.
	/// Returns <see langword="false"/> for anything else, including a value that is absent.
	/// </summary>
	/// <remarks>
	/// Deliberately not <see cref="Enum.TryParse{T}(string, bool, out T)"/>: that accepts numeric
	/// text, so <c>"42"</c> yields an undefined status and — worse — <c>"0"</c> yields
	/// <see cref="AccountStatus.Active"/>, failing open on the field that gates authentication.
	/// <see cref="Enum.IsDefined{T}"/> does not catch the second case, because 0 is defined.
	/// Only a name is accepted, which is the only form ever written.
	/// </remarks>
	public static bool TryParseName(string? value, out AccountStatus status)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			var trimmed = value.Trim();
			foreach (var candidate in Enum.GetValues<AccountStatus>())
			{
				if (string.Equals(candidate.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
				{
					status = candidate;
					return true;
				}
			}
		}

		status = AccountStatus.Disabled;
		return false;
	}
}
