using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Plugins.Discord.Storage;

namespace SharpMUSH.Plugins.Discord.Members;

/// <summary>The character a linked Discord user speaks as in one linked channel.</summary>
/// <param name="Character">The character, by objid.</param>
public sealed record DiscordSpeaker(ulong ChannelId, string Character);

/// <summary>A linked Discord user's speaking identities, at most one per linked channel.</summary>
public sealed record DiscordSpeakerRecord(ulong UserId, IReadOnlyList<DiscordSpeaker> Channels);

/// <summary>
/// Speaking identities: whom a linked Discord user's messages in a linked channel are spoken by in the game. Each is
/// chosen by the character itself (<c>@discord/speak</c>), never inferred from the account, so a player's other
/// characters stay private.
/// </summary>
public interface IDiscordSpeakerStore
{
	/// <summary>The objid <paramref name="userId"/> speaks as in <paramref name="channelId"/>, if any.</summary>
	string? Character(ulong userId, ulong channelId);

	/// <summary>Makes <paramref name="character"/> the user's identity in the channel, replacing any other there.</summary>
	ValueTask SetAsync(ulong userId, ulong channelId, string character, CancellationToken cancellationToken = default);

	/// <returns>Whether the user had an identity in the channel.</returns>
	ValueTask<bool> ClearAsync(ulong userId, ulong channelId, CancellationToken cancellationToken = default);

	/// <summary>Drops every identity of the user, when it unlinks.</summary>
	ValueTask ForgetAsync(ulong userId, CancellationToken cancellationToken = default);
}

/// <summary>One plugin-owned table, <c>discord.speaker</c>, by Discord user id.</summary>
public sealed class LightningDiscordSpeakerStore(ILightningStorageAccessor accessor) : IDiscordSpeakerStore
{
	private readonly DiscordTable<DiscordSpeakerRecord> _speakers = new(accessor, "discord.speaker");

	/// <summary>A change reads the user's record and writes it back; two at once must not lose one.</summary>
	private readonly SemaphoreSlim _changing = new(1, 1);

	public string? Character(ulong userId, ulong channelId)
		=> _speakers.Get(userId)?.Channels.FirstOrDefault(speaker => speaker.ChannelId == channelId)?.Character;

	public ValueTask SetAsync(ulong userId, ulong channelId, string character, CancellationToken cancellationToken = default)
		=> ChangeAsync(userId, channels => [.. channels.Where(speaker => speaker.ChannelId != channelId),
			new DiscordSpeaker(channelId, character)], cancellationToken);

	public async ValueTask<bool> ClearAsync(ulong userId, ulong channelId, CancellationToken cancellationToken = default)
	{
		var had = Character(userId, channelId) is not null;
		await ChangeAsync(userId, channels => [.. channels.Where(speaker => speaker.ChannelId != channelId)], cancellationToken);
		return had;
	}

	public async ValueTask ForgetAsync(ulong userId, CancellationToken cancellationToken = default)
		=> await _speakers.RemoveAsync(userId, cancellationToken);

	private async ValueTask ChangeAsync(ulong userId, Func<IReadOnlyList<DiscordSpeaker>, IReadOnlyList<DiscordSpeaker>> change,
		CancellationToken cancellationToken)
	{
		await _changing.WaitAsync(cancellationToken);
		try
		{
			var channels = change(_speakers.Get(userId)?.Channels ?? []);
			if (channels.Count == 0)
			{
				await _speakers.RemoveAsync(userId, CancellationToken.None);
			}
			else
			{
				await _speakers.SaveAsync(userId, new DiscordSpeakerRecord(userId, channels), CancellationToken.None);
			}
		}
		finally
		{
			_changing.Release();
		}
	}
}
