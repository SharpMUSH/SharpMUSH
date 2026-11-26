namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>
/// A Discord user linked to a game account. Each side has at most one link. The record stays after an unlink, with
/// <see cref="UnlinkedAt"/> set, so the same Discord user cannot carry a mapped role to another account at once.
/// </summary>
/// <param name="UserId">Discord's id for the user, which never changes.</param>
/// <param name="AccountId">The game account.</param>
/// <param name="Username">The Discord username when it was linked, for listings.</param>
/// <param name="LinkedAt">When the account confirmed it.</param>
/// <param name="UnlinkedAt">When it was unlinked; null while it is linked.</param>
public sealed record DiscordAccountLink(
	ulong UserId,
	string AccountId,
	string Username,
	DateTimeOffset LinkedAt,
	DateTimeOffset? UnlinkedAt = null)
{
	/// <summary>Whether the link holds now.</summary>
	public bool Linked => UnlinkedAt is null;
}

/// <summary>Which Discord user is linked to which game account, for plugins that build on the Discord plugin.</summary>
public interface IDiscordLinkedAccounts
{
	/// <summary>The link of Discord user <paramref name="userId"/>, or null while they are not linked.</summary>
	DiscordAccountLink? Find(ulong userId);

	/// <summary>The Discord user linked to the game account <paramref name="accountId"/> now, or null.</summary>
	DiscordAccountLink? ForAccount(string accountId);
}
