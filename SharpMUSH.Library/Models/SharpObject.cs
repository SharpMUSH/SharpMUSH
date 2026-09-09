using SharpMUSH.Library.Extensions;
using DotNext.Threading;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpObject : IObjectShaped<SharpObject>
{
	[JsonIgnore]
	public string? Id { get; set; }

	[JsonPropertyName("_key")]
	public int Key { get; set; }

	public DBRef DBRef => new(Key, CreationTime);

	public required string Name { get; set; }

	public required string Type { get; set; }

	/// <summary>
	/// Keyed by lock name. PennMUSH matches a lock name with <c>strcasecmp</c>
	/// (<c>src/lock.c:364</c>, <c>getlockstruct</c>), so every provider builds this dictionary with
	/// <see cref="LockNameComparer"/>: <c>@lock/dropto</c> stores the canonical <c>Dropto</c> and a
	/// reader asking for <see cref="LockType.DropTo"/> must still find it.
	/// </summary>
	public required IImmutableDictionary<string, SharpLockData> Locks { get; set; }

	/// <summary>
	/// How lock names compare — case-insensitively, per <c>src/lock.c:364</c>.
	/// </summary>
	public static StringComparer LockNameComparer => StringComparer.OrdinalIgnoreCase;

	/// <summary>
	/// An empty lock dictionary that compares its keys the way <see cref="Locks"/> must.
	/// </summary>
	public static IImmutableDictionary<string, SharpLockData> EmptyLocks { get; }
		= ImmutableDictionary.Create<string, SharpLockData>(LockNameComparer);

	/// <summary>
	/// Folds lock names into a <see cref="LockNameComparer"/> dictionary. A stored world can hold two
	/// names differing only in case, and the plain <see cref="Dictionary{TKey,TValue}"/> constructor
	/// throws <see cref="ArgumentException"/> on that pair — a crash on every hydration of the object,
	/// so every provider funnels its lock dictionaries through here instead. The first name in source
	/// order wins, as <c>getlockstruct</c> returns the first <c>strcasecmp</c> hit walking the lock
	/// list (<c>src/lock.c:364</c>); <paramref name="onCollision"/> is handed the winning and dropped
	/// names so the caller can report that the world needs cleaning.
	/// </summary>
	public static Dictionary<string, TValue> FoldLockNames<TValue>(
		IEnumerable<KeyValuePair<string, TValue>> source,
		Action<string, string>? onCollision = null)
	{
		var folded = new Dictionary<string, TValue>(LockNameComparer);
		// A Dictionary keeps the key it first stored, so the winning spellings are tracked alongside
		// to name both halves of a collision.
		var spellings = new Dictionary<string, string>(LockNameComparer);

		foreach (var (name, value) in source)
		{
			if (!folded.TryAdd(name, value))
			{
				onCollision?.Invoke(spellings[name], name);
				continue;
			}

			spellings[name] = name;
		}

		return folded;
	}

	public long CreationTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();

	public long ModifiedTime { get; set; } = DateTimeOffset.Now.ToUnixTimeMilliseconds();

	/// <summary>
	/// Warning types enabled for this object. If None, the owner's warnings are used.
	/// </summary>
	public WarningType Warnings { get; set; } = WarningType.None;

	[JsonIgnore]
	public required AsyncRelation<SharpPlayer> Owner { get; set; }

	[JsonIgnore]
	public required Lazy<IAsyncEnumerable<SharpPower>> Powers { get; set; }

	[JsonIgnore]
	public required Lazy<IAsyncEnumerable<SharpAttribute>> Attributes { get; set; }

	[JsonIgnore]
	public required Lazy<IAsyncEnumerable<LazySharpAttribute>> LazyAttributes { get; set; }

	[JsonIgnore]
	public required Lazy<IAsyncEnumerable<SharpAttribute>> AllAttributes { get; set; }

	[JsonIgnore]
	public required Lazy<IAsyncEnumerable<LazySharpAttribute>> LazyAllAttributes { get; set; }

	[JsonIgnore]
	public required Lazy<IAsyncEnumerable<SharpObjectFlag>> Flags { get; set; }

	[JsonIgnore]
	public required AsyncRelation<AnyOptionalSharpObject> Parent { get; set; }

	[JsonIgnore]
	public required AsyncRelation<AnyOptionalSharpObject> Zone { get; set; }

	[JsonIgnore]
	public required Lazy<IAsyncEnumerable<SharpObject>?> Children { get; set; }

	// A loaded object is a snapshot. A command that mutates one updates the instance it holds through
	// these, and invalidates the object's cache key; nothing re-reads storage through the instance.
	// Relations to other objects (Location, Home, Owner, Parent, Zone) are resolved on every read and
	// need no update here.

	public async ValueTask WithFlag(SharpObjectFlag flag, CancellationToken cancellationToken = default)
	{
		var flags = await Flags.Value.ToListAsync(cancellationToken);
		if (!flags.Any(f => f.Name.Equals(flag.Name, StringComparison.OrdinalIgnoreCase)))
		{
			flags.Add(flag);
		}

		Flags = new(() => flags.ToAsyncEnumerable());
	}

	public async ValueTask WithoutFlag(string flagName, CancellationToken cancellationToken = default)
	{
		var flags = await Flags.Value
			.Where(f => !f.Name.Equals(flagName, StringComparison.OrdinalIgnoreCase))
			.ToListAsync(cancellationToken);
		Flags = new(() => flags.ToAsyncEnumerable());
	}

	public async ValueTask WithPower(SharpPower power, CancellationToken cancellationToken = default)
	{
		var powers = await Powers.Value.ToListAsync(cancellationToken);
		if (!powers.Any(p => string.Equals(p.Name, power.Name, StringComparison.OrdinalIgnoreCase)))
		{
			powers.Add(power);
		}

		Powers = new(() => powers.ToAsyncEnumerable());
	}

	public async ValueTask WithoutPower(string powerName, CancellationToken cancellationToken = default)
	{
		var powers = await Powers.Value
			.Where(p => !string.Equals(p.Name, powerName, StringComparison.OrdinalIgnoreCase))
			.ToListAsync(cancellationToken);
		Powers = new(() => powers.ToAsyncEnumerable());
	}

	public void WithLock(string lockName, SharpLockData data) => Locks = Locks.SetItem(lockName, data);

	public void WithoutLock(string lockName) => Locks = Locks.Remove(lockName);

	public static DBRef? RefOf(SharpObject value) => value.DBRef;

	public static bool TryFromNode(AnyOptionalSharpObject node, out SharpObject value)
	{
		value = node.IsNone ? null! : node.Known.Object();
		return !node.IsNone;
	}
}
