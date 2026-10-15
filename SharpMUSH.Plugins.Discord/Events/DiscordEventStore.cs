using System.Text;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Plugins.Discord.Storage;

namespace SharpMUSH.Plugins.Discord.Events;

/// <summary>A game key (a scene id, say) and the Discord Scheduled Event published for it.</summary>
public sealed record DiscordEventRecord(string Key, ulong GuildId, ulong EventId, string Name, DateTimeOffset Start, DateTimeOffset PublishedAt);

public interface IDiscordEventStore
{
	IReadOnlyList<DiscordEventRecord> List();

	DiscordEventRecord? Get(string key);

	ValueTask SaveAsync(DiscordEventRecord record, CancellationToken cancellationToken = default);

	ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>
/// The plugin-owned table <c>discord.event</c>. Its keys are Discord-sized numbers, so a game key is stored under a hash
/// of it and kept in the record, which is checked on every read.
/// </summary>
public sealed class LightningDiscordEventStore(ILightningStorageAccessor accessor) : IDiscordEventStore
{
	private readonly DiscordTable<DiscordEventRecord> _events = new(accessor, "discord.event");

	public IReadOnlyList<DiscordEventRecord> List() => _events.List();

	public DiscordEventRecord? Get(string key) => _events.Get(Hash(key)) is { } record && record.Key == key ? record : null;

	public ValueTask SaveAsync(DiscordEventRecord record, CancellationToken cancellationToken = default)
		=> _events.SaveAsync(Hash(record.Key), record, cancellationToken);

	public async ValueTask<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
		=> Get(key) is not null && await _events.RemoveAsync(Hash(key), cancellationToken);

	/// <summary>FNV-1a over the key's UTF-8 bytes: stable across runs, unlike <see cref="string.GetHashCode()"/>.</summary>
	public static ulong Hash(string key)
	{
		var hash = 14695981039346656037UL;
		foreach (var b in Encoding.UTF8.GetBytes(key))
		{
			hash = (hash ^ b) * 1099511628211UL;
		}

		return hash;
	}
}
