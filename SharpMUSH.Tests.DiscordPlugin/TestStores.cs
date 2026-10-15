using System.Collections.Concurrent;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>Speaking identities in memory; the Lightning store is tested on its own.</summary>
public sealed class MemorySpeakerStore : IDiscordSpeakerStore
{
	private readonly ConcurrentDictionary<(ulong User, ulong Channel), string> _speakers = new();

	public string? Character(ulong userId, ulong channelId) => _speakers.GetValueOrDefault((userId, channelId));

	public ValueTask SetAsync(ulong userId, ulong channelId, string character, CancellationToken cancellationToken = default)
	{
		_speakers[(userId, channelId)] = character;
		return ValueTask.CompletedTask;
	}

	public ValueTask<bool> ClearAsync(ulong userId, ulong channelId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(_speakers.TryRemove((userId, channelId), out _));

	public ValueTask ForgetAsync(ulong userId, CancellationToken cancellationToken = default)
	{
		foreach (var key in _speakers.Keys.Where(key => key.User == userId))
		{
			_speakers.TryRemove(key, out _);
		}

		return ValueTask.CompletedTask;
	}
}

/// <summary>Message pairs in memory.</summary>
public sealed class MemoryMessagePairs : IDiscordMessagePairs
{
	private readonly ConcurrentDictionary<ulong, DiscordMessagePair> _pairs = new();

	public IReadOnlyCollection<DiscordMessagePair> All => [.. _pairs.Values];

	public DiscordMessagePair? Get(ulong messageId) => _pairs.GetValueOrDefault(messageId);

	public ValueTask SaveAsync(DiscordMessagePair pair, CancellationToken cancellationToken = default)
	{
		_pairs[pair.MessageId] = pair;
		return ValueTask.CompletedTask;
	}

	public ValueTask<bool> RemoveAsync(ulong messageId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(_pairs.TryRemove(messageId, out _));

	public ValueTask<int> ForgetBeforeAsync(DateTimeOffset before, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(0);
}
