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
	public bool Linked => UnlinkedAt is null;
}

/// <summary>The Linked role the bot made in a server, so a renamed role is still the one it gives.</summary>
public sealed record DiscordGuildRecord(ulong GuildId, ulong LinkedRoleId);
