using System.Collections.Concurrent;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Palette;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IContextStack"/>
/// <remarks>
/// Keyed by the character's dbref number with its creation stamp, so a recycled number starts empty.
/// Nothing is stored: a restart empties every stack, and the next command or event fills it again.
/// </remarks>
public sealed class ContextStack(TimeProvider? timeProvider = null) : IContextStack
{
	private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
	private readonly ConcurrentDictionary<DBRef, List<PlayerContext>> _stacks = new();

	private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

	public void Push(DBRef character, string name, IReadOnlyDictionary<string, string>? values = null,
		TimeSpan? lifetime = null, string? info = null)
	{
		var key = name.Trim().ToLowerInvariant();
		var now = _time.GetUtcNow();
		var stack = _stacks.GetOrAdd(character, static _ => []);
		lock (stack)
		{
			Prune(stack, now);
			stack.RemoveAll(context => context.Name == key);
			stack.Insert(0, new PlayerContext(key, values ?? NoValues, now,
				lifetime is { } given && given > TimeSpan.Zero ? given : null,
				string.IsNullOrWhiteSpace(info) ? null : info));
			if (stack.Count > ContextWeights.Depth) stack.RemoveRange(ContextWeights.Depth, stack.Count - ContextWeights.Depth);
		}
	}

	public bool Drop(DBRef character, string name)
	{
		if (!_stacks.TryGetValue(character, out var stack)) return false;
		var key = name.Trim().ToLowerInvariant();
		lock (stack) return stack.RemoveAll(context => context.Name == key) > 0;
	}

	public IReadOnlyList<WeightedContext> Read(DBRef character)
	{
		if (!_stacks.TryGetValue(character, out var stack)) return [];
		var now = _time.GetUtcNow();
		lock (stack)
		{
			Prune(stack, now);
			return [.. stack.Select(context => new WeightedContext(context, ContextWeights.At(context.PushedAt, now)))];
		}
	}

	/// <summary>Removes what has sunk below the floor or outlived its lifetime.</summary>
	private static void Prune(List<PlayerContext> stack, DateTimeOffset now)
		=> stack.RemoveAll(context => ContextWeights.At(context.PushedAt, now) < ContextWeights.Floor
			|| (context.Lifetime is { } lifetime && now - context.PushedAt >= lifetime));
}
