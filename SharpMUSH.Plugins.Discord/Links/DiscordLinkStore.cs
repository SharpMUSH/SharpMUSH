using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Plugins.Discord.Storage;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>The linked Discord channels, kept in the world so its backups carry them.</summary>
public interface IDiscordLinkStore
{
	IReadOnlyList<DiscordChannelLink> List();

	DiscordChannelLink? Get(ulong channelId);

	/// <summary>Adds the link, or replaces the one for the same channel.</summary>
	ValueTask SaveAsync(DiscordChannelLink link, CancellationToken cancellationToken = default);

	/// <returns>Whether there was a link to remove.</returns>
	ValueTask<bool> RemoveAsync(ulong channelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// One plugin-owned table, <c>discord.link</c>: the channel id, as eight big-endian bytes, to the link as JSON.
/// </summary>
public sealed class LightningDiscordLinkStore(ILightningStorageAccessor accessor) : IDiscordLinkStore
{
	private readonly DiscordTable<DiscordChannelLink> _links = new(accessor, "discord.link");

	public IReadOnlyList<DiscordChannelLink> List() => _links.List();

	public DiscordChannelLink? Get(ulong channelId) => _links.Get(channelId);

	public ValueTask SaveAsync(DiscordChannelLink link, CancellationToken cancellationToken = default)
		=> _links.SaveAsync(link.ChannelId, link, cancellationToken);

	public ValueTask<bool> RemoveAsync(ulong channelId, CancellationToken cancellationToken = default)
		=> _links.RemoveAsync(channelId, cancellationToken);
}
