using Discord;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Plugins.Discord.Storage;

namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>A message the bot posted in a linked channel, and the feed lines it carries.</summary>
public sealed record DiscordMessagePair(ulong MessageId, ulong ChannelId, IReadOnlyList<long> LineIds);

/// <summary>
/// Which feed lines each posted message carries, so a moderator deleting the message in Discord takes the lines out of
/// the game's recall too. Kept in the world, so a restart loses none.
/// </summary>
public interface IDiscordMessagePairs
{
	DiscordMessagePair? Get(ulong messageId);

	ValueTask SaveAsync(DiscordMessagePair pair, CancellationToken cancellationToken = default);

	/// <returns>Whether there was a pair to remove.</returns>
	ValueTask<bool> RemoveAsync(ulong messageId, CancellationToken cancellationToken = default);

	/// <summary>Forgets the messages posted before <paramref name="before"/>.</summary>
	/// <returns>How many went.</returns>
	ValueTask<int> ForgetBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default);
}

/// <summary>One plugin-owned table, <c>discord.message</c>, by Discord message id.</summary>
public sealed class LightningDiscordMessagePairs(ILightningStorageAccessor accessor) : IDiscordMessagePairs
{
	private readonly DiscordTable<DiscordMessagePair> _pairs = new(accessor, "discord.message");

	public DiscordMessagePair? Get(ulong messageId) => _pairs.Get(messageId);

	public ValueTask SaveAsync(DiscordMessagePair pair, CancellationToken cancellationToken = default)
		=> _pairs.SaveAsync(pair.MessageId, pair, cancellationToken);

	public ValueTask<bool> RemoveAsync(ulong messageId, CancellationToken cancellationToken = default)
		=> _pairs.RemoveAsync(messageId, cancellationToken);

	public ValueTask<int> ForgetBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
		=> _pairs.RemoveBelowAsync(SnowflakeUtils.ToSnowflake(before), cancellationToken);
}
