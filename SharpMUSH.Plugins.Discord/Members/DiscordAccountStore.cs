using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Plugins.Discord.Storage;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>Linked Discord users, and the Linked role in each server, kept in the world so its backups carry them.</summary>
public interface IDiscordAccountStore
{
	/// <summary>Every record, unlinked ones included.</summary>
	IReadOnlyList<DiscordAccountLink> List();

	DiscordAccountLink? Get(ulong userId);

	/// <summary>The Discord user linked to <paramref name="accountId"/> now, if any.</summary>
	DiscordAccountLink? ForAccount(string accountId);

	ValueTask SaveAsync(DiscordAccountLink link, CancellationToken cancellationToken = default);

	DiscordGuildRecord? Guild(ulong guildId);

	ValueTask SaveGuildAsync(DiscordGuildRecord guild, CancellationToken cancellationToken = default);
}

/// <summary>Two plugin-owned tables: <c>discord.account</c> by Discord user id and <c>discord.guild</c> by server id.</summary>
public sealed class LightningDiscordAccountStore(ILightningStorageAccessor accessor) : IDiscordAccountStore
{
	private readonly DiscordTable<DiscordAccountLink> _accounts = new(accessor, "discord.account");
	private readonly DiscordTable<DiscordGuildRecord> _guilds = new(accessor, "discord.guild");

	public IReadOnlyList<DiscordAccountLink> List() => _accounts.List();

	public DiscordAccountLink? Get(ulong userId) => _accounts.Get(userId);

	public DiscordAccountLink? ForAccount(string accountId)
		=> _accounts.List().FirstOrDefault(link => link.Linked && link.AccountId == accountId);

	public ValueTask SaveAsync(DiscordAccountLink link, CancellationToken cancellationToken = default)
		=> _accounts.SaveAsync(link.UserId, link, cancellationToken);

	public DiscordGuildRecord? Guild(ulong guildId) => _guilds.Get(guildId);

	public ValueTask SaveGuildAsync(DiscordGuildRecord guild, CancellationToken cancellationToken = default)
		=> _guilds.SaveAsync(guild.GuildId, guild, cancellationToken);
}
