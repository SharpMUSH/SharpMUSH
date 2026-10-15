using System.Text.Json;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;

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
public sealed class LightningDiscordLinkStore : IDiscordLinkStore
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

	private readonly ILightningStorageAccessor _accessor;
	private readonly TableDef _links;

	public LightningDiscordLinkStore(ILightningStorageAccessor accessor)
	{
		_accessor = accessor;
		// Opening a table must not happen inside a write job, so it happens here, once.
		_links = accessor.OpenTable("discord.link", duplicates: false);
	}

	public IReadOnlyList<DiscordChannelLink> List()
		=> _accessor.Read(tx => tx.Range(_links, []).Select(entry => Deserialize(entry.Value)).OfType<DiscordChannelLink>().ToList());

	public DiscordChannelLink? Get(ulong channelId)
		=> _accessor.Read(tx => tx.TryGet(_links, Key(channelId), out var value) ? Deserialize(value) : null);

	public async ValueTask SaveAsync(DiscordChannelLink link, CancellationToken cancellationToken = default)
	{
		var value = JsonSerializer.SerializeToUtf8Bytes(link, JsonOptions);
		await _accessor.WriteAsync(tx =>
		{
			tx.Put(_links, Key(link.ChannelId), value);
			return true;
		}, cancellationToken);
	}

	public async ValueTask<bool> RemoveAsync(ulong channelId, CancellationToken cancellationToken = default)
		=> await _accessor.WriteAsync(tx => tx.Delete(_links, Key(channelId)), cancellationToken);

	private static byte[] Key(ulong channelId)
	{
		var key = new byte[sizeof(ulong)];
		System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(key, channelId);
		return key;
	}

	/// <summary>A record this version cannot read is left out rather than stopping every other link.</summary>
	private static DiscordChannelLink? Deserialize(byte[] value)
	{
		try
		{
			return JsonSerializer.Deserialize<DiscordChannelLink>(value, JsonOptions);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
