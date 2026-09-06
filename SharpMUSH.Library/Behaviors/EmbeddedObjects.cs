using OneOf;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using System.Collections;

namespace SharpMUSH.Library.Behaviors;

/// <summary>
/// Finds the objects a cached result embeds, so the entry can carry one
/// <see cref="CacheKeys.ObjectTag"/> per object and be expired when any of them is invalidated.
/// </summary>
/// <remarks>
/// A loaded <see cref="SharpObject"/> is a snapshot (flags, powers, locks, name), and the same
/// object sits inside many cached results: its own node, a room's contents list, an occupant's
/// location answer, a player-by-name lookup. A write removes the node's key; the tag is what
/// reaches the rest. Only the value's own shape is walked - an object, a typed object, a union of
/// them, or a sequence of those - never the lazies hanging off an object.
/// </remarks>
public static class EmbeddedObjects
{
	public static string[] TagsFor(object? value)
	{
		var numbers = new HashSet<int>();
		Collect(value, numbers);
		return numbers.Select(CacheKeys.ObjectTag).ToArray();
	}

	private static void Collect(object? value, HashSet<int> numbers)
	{
		switch (value)
		{
			case null:
			case string:
				return;
			case SharpObject obj:
				numbers.Add(obj.Key);
				return;
			case SharpPlayer p:
				numbers.Add(p.Object.Key);
				return;
			case SharpRoom r:
				numbers.Add(r.Object.Key);
				return;
			case SharpExit e:
				numbers.Add(e.Object.Key);
				return;
			case SharpThing t:
				numbers.Add(t.Object.Key);
				return;
			case IOneOf union:
				Collect(union.Value, numbers);
				return;
			case IEnumerable items:
				foreach (var item in items)
				{
					Collect(item, numbers);
				}

				return;
		}
	}
}
