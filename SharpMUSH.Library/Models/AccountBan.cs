namespace SharpMUSH.Library.Models;

/// <summary>
/// Why an account is banned, who banned it and until when. A banned account is
/// <see cref="AccountStatus.Disabled"/>; this record is what a plain disable lacks. It goes away when the
/// ban is lifted, when it expires, or when anything else changes the account's status.
/// </summary>
/// <param name="AccountId">The banned account.</param>
/// <param name="Reason">What staff gave as the reason.</param>
/// <param name="BannedBy">The account of the staff member who banned it.</param>
/// <param name="At">When it was banned.</param>
/// <param name="ExpiresAt">When the ban lifts by itself, or null for a ban that stays until lifted.</param>
public sealed record AccountBan(
	string AccountId,
	string Reason,
	string? BannedBy,
	DateTimeOffset At,
	DateTimeOffset? ExpiresAt)
{
	/// <summary>Whether the ban has run out at <paramref name="now"/>.</summary>
	public bool HasExpired(DateTimeOffset now) => ExpiresAt is { } expires && expires <= now;
}

/// <summary>
/// The credentials were right, but the account may not sign in: it is disabled, banned or closed.
/// </summary>
/// <param name="Account">The account.</param>
/// <param name="Ban">Its ban, when it is banned rather than only disabled.</param>
public sealed record AccountUnavailable(SharpAccount Account, AccountBan? Ban)
{
	/// <summary>What the person signing in is told.</summary>
	public string Message => Account.Status switch
	{
		AccountStatus.Disabled when Ban is { ExpiresAt: { } expires } =>
			$"This account is banned until {expires.UtcDateTime:yyyy-MM-dd HH:mm} UTC.",
		AccountStatus.Disabled when Ban is not null => "This account is banned.",
		AccountStatus.Disabled => "This account is disabled.",
		_ => "This account is closed."
	};
}
