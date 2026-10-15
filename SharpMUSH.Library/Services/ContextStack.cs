using System.Collections.Concurrent;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Palette;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="IContextStack"/>
/// <remarks>
/// Keyed by the character's dbref number with its creation stamp, so a recycled number starts empty.
/// Nothing is stored: a restart empties every stack, and the next command or event fills it again.
/// The stack bounds itself whoever pushes: a name, its values and its info are cut to their limits, a
/// stack holds <see cref="ContextWeights.Depth"/>, an emptied stack is let go, and past
/// <paramref name="maxCharacters"/> stacks the one pushed to least recently goes.
/// </remarks>
public sealed class ContextStack(TimeProvider? timeProvider = null, int maxCharacters = ContextWeights.MaxCharacters)
	: IContextStack
{
	private sealed class Stack
	{
		public readonly List<PlayerContext> Contexts = [];
		public DateTimeOffset LastPush;
	}

	private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
	private readonly ConcurrentDictionary<DBRef, Stack> _stacks = new();

	private static readonly IReadOnlyDictionary<string, string> NoValues = new Dictionary<string, string>();

	public void Push(DBRef character, string name, IReadOnlyDictionary<string, string>? values = null,
		TimeSpan? lifetime = null, string? info = null)
	{
		var key = Cut(name.Trim().ToLowerInvariant(), ContextWeights.MaxNameLength);
		if (key.Length == 0) return;

		var now = _time.GetUtcNow();
		while (true)
		{
			var stack = _stacks.GetOrAdd(character, static _ => new Stack());
			lock (stack)
			{
				// A drop or read that emptied this stack let it go meanwhile; push onto the one that replaced it.
				if (!_stacks.TryGetValue(character, out var current) || current != stack) continue;
				PushOnto(stack, key, values, lifetime, info, now);
			}

			break;
		}

		if (_stacks.Count > maxCharacters) EvictStalest(character);
	}

	private static void PushOnto(Stack stack, string key, IReadOnlyDictionary<string, string>? values,
		TimeSpan? lifetime, string? info, DateTimeOffset now)
	{
		Prune(stack.Contexts, now);
		stack.Contexts.RemoveAll(context => context.Name == key);
		stack.Contexts.Insert(0, new PlayerContext(key, Bounded(values), now,
			lifetime is { } given && given > TimeSpan.Zero
				? (given > ContextWeights.MaxLifetime ? ContextWeights.MaxLifetime : given)
				: null,
			string.IsNullOrWhiteSpace(info) ? null : Cut(info, ContextWeights.MaxInfoLength)));
		if (stack.Contexts.Count > ContextWeights.Depth)
			stack.Contexts.RemoveRange(ContextWeights.Depth, stack.Contexts.Count - ContextWeights.Depth);
		stack.LastPush = now;
	}

	public bool Drop(DBRef character, string name)
	{
		if (!_stacks.TryGetValue(character, out var stack)) return false;
		var key = name.Trim().ToLowerInvariant();
		bool dropped;
		lock (stack)
		{
			dropped = stack.Contexts.RemoveAll(context => context.Name == key) > 0;
			if (stack.Contexts.Count == 0) _stacks.TryRemove(new KeyValuePair<DBRef, Stack>(character, stack));
		}

		return dropped;
	}

	public IReadOnlyList<WeightedContext> Read(DBRef character)
	{
		if (!_stacks.TryGetValue(character, out var stack)) return [];
		var now = _time.GetUtcNow();
		lock (stack)
		{
			Prune(stack.Contexts, now);
			if (stack.Contexts.Count == 0)
			{
				_stacks.TryRemove(new KeyValuePair<DBRef, Stack>(character, stack));
				return [];
			}

			return [.. stack.Contexts.Select(context => new WeightedContext(context, ContextWeights.At(context.PushedAt, now)))];
		}
	}

	/// <summary>Lets go of the stack pushed to least recently, never the one just pushed to.</summary>
	private void EvictStalest(DBRef pushed)
	{
		var stalest = _stacks
			.Where(pair => pair.Key != pushed)
			.MinBy(pair => pair.Value.LastPush);
		if (stalest.Value is not null) _stacks.TryRemove(stalest);
	}

	/// <summary>At most <see cref="ContextWeights.MaxValues"/> values, each name and value cut to its limit.</summary>
	private static IReadOnlyDictionary<string, string> Bounded(IReadOnlyDictionary<string, string>? values)
		=> values is null || values.Count == 0
			? NoValues
			: values
				.Take(ContextWeights.MaxValues)
				.ToDictionary(pair => Cut(pair.Key, ContextWeights.MaxNameLength), pair => Cut(pair.Value, ContextWeights.MaxValueLength));

	private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;

	/// <summary>Removes what has sunk below the floor or outlived its lifetime.</summary>
	private static void Prune(List<PlayerContext> stack, DateTimeOffset now)
		=> stack.RemoveAll(context => ContextWeights.At(context.PushedAt, now) < ContextWeights.Floor
			|| (context.Lifetime is { } lifetime && now - context.PushedAt >= lifetime));
}
