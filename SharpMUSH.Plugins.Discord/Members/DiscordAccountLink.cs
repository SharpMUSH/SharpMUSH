namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>The Linked role the bot made in a server, so a renamed role is still the one it gives.</summary>
public sealed record DiscordGuildRecord(ulong GuildId, ulong LinkedRoleId);
