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

	public required IImmutableDictionary<string, SharpLockData> Locks { get; set; }

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
		var flags = (await Flags.Value.ToListAsync(cancellationToken))
			.Where(f => !f.Name.Equals(flagName, StringComparison.OrdinalIgnoreCase))
			.ToList();
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
		var powers = (await Powers.Value.ToListAsync(cancellationToken))
			.Where(p => !string.Equals(p.Name, powerName, StringComparison.OrdinalIgnoreCase))
			.ToList();
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
