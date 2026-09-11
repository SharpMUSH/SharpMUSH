using System.Runtime.CompilerServices;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="INavigationStore"/>: graph traversal between objects. Ported from
/// <c>SurrealDatabase.Navigation.cs</c> (and, for exits/entrances/homed-at, <c>SurrealDatabase.Objects.cs</c>)
/// onto the edge tables declared in <c>LightningDatabase.Objects.cs</c>:
/// <see cref="Tables.Location"/> (object → its container, reverse → contents),
/// <see cref="Tables.Home"/> (object → its home/drop-to/destination, reverse → what homes there),
/// <see cref="Tables.Exit"/> (room → its exits, no reverse walk needed), and
/// <see cref="Tables.Parent"/>/<see cref="Tables.Zone"/> (object → parent/zone, reverse → children/zoned objects).
/// <see cref="IsReachableViaParentOrZoneAsync"/> lives in <c>LightningDatabase.Objects.cs</c> — it is part of
/// <c>IObjectStore</c>, not this store, and sits alongside object deletion.
/// </summary>
public partial class LightningDatabase
{
	public ValueTask<AnyOptionalSharpObject> GetParentAsync(string id, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(GetOptionalRelatedCore(Tables.Parent.Forward, ParseDbref(id)));

	public ValueTask<SharpPlayer> GetObjectOwnerAsync(string id, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(GetOwnerCore(ParseDbref(id)));

	public ValueTask<AnyOptionalSharpObject> GetZoneAsync(string id, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(GetOptionalRelatedCore(Tables.Zone.Forward, ParseDbref(id)));

	// GetHomeAsync/GetDropToAsync/GetExitDestinationAsync all read the same has_home-equivalent edge
	// (Tables.Home.Forward) — a room's drop-to and an exit's destination both reuse the home edge, same
	// as SurrealDatabase.cs does. They differ only in whether a missing edge is an error (a home is
	// mandatory for a player/thing) or a legitimate absence (an unset drop-to, or a freshly @open'd or
	// @unlink'd exit).
	public ValueTask<AnySharpContainer> GetHomeAsync(string typedId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(GetRequiredContainerRelation(Tables.Home.Forward, ParseDbref(typedId)));

	public ValueTask<AnyOptionalSharpContainer> GetDropToAsync(string roomTypedId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(GetOptionalContainerRelation(Tables.Home.Forward, ParseDbref(roomTypedId)));

	public ValueTask<AnyOptionalSharpContainer> GetExitDestinationAsync(string exitTypedId, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(GetOptionalContainerRelation(Tables.Home.Forward, ParseDbref(exitTypedId)));

	public IAsyncEnumerable<SharpObject> GetParentsAsync(string id, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpObject>(ct => GetParentsCoreAsync(ParseDbref(id), ct));

	/// <summary>
	/// Walks the parent chain exactly as <c>SurrealDatabase.GetParentsAsync</c> does: a visited set keyed
	/// on the node about to be expanded (starting with <paramref name="dbref"/> itself), so a cycle is
	/// caught the moment the walk would revisit a node rather than only once a parent repeats. A 100-hop
	/// cap bounds the work independent of that set, matching the <c>maxDepth</c> default every other
	/// bounded graph walk in this store uses (<see cref="IsReachableViaParentOrZoneAsync"/>).
	/// </summary>
	private async IAsyncEnumerable<SharpObject> GetParentsCoreAsync(long dbref, [EnumeratorCancellation] CancellationToken ct)
	{
		var chain = Store.Read(tx =>
		{
			var result = new List<SharpObject>();
			var visited = new HashSet<long>();
			var current = dbref;
			var hops = 0;

			while (hops < 100)
			{
				if (!visited.Add(current))
				{
					break;
				}

				var parentKey = GetSingleEdge(tx, Tables.Parent.Forward, current);
				if (parentKey is null)
				{
					break;
				}

				var found = ReadObject(tx, parentKey.Value);
				if (found is null)
				{
					break;
				}

				result.Add(MapToSharpObject(found.Value.Dbref, found.Value.Record));
				current = parentKey.Value;
				hops++;
			}

			return result;
		});

		foreach (var parent in chain)
		{
			ct.ThrowIfCancellationRequested();
			yield return parent;
		}
	}

	public IAsyncEnumerable<SharpExit> GetEntrancesAsync(DBRef destination, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpExit>(ct => GetEntrancesCoreAsync(destination.Number, ct));

	private async IAsyncEnumerable<SharpExit> GetEntrancesCoreAsync(long destinationKey, [EnumeratorCancellation] CancellationToken ct)
	{
		var exits = Store.Read(tx => tx.Dups(Tables.Home.Reverse, Keys.Dbref(destinationKey))
			.Select(v => Keys.ReadDbref(v))
			.Select(key => ReadObject(tx, key))
			.Where(found => found is not null && found.Value.Record.Type == DatabaseConstants.TypeExit)
			.Select(found => Hydrate(found!.Value.Dbref, found.Value.Record).AsExit)
			.ToList());

		foreach (var exit in exits)
		{
			ct.ThrowIfCancellationRequested();
			yield return exit;
		}
	}

	public IAsyncEnumerable<AnySharpContent> GetHomedAtAsync(DBRef home, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AnySharpContent>(ct => GetHomedAtCoreAsync(home.Number, ct));

	private async IAsyncEnumerable<AnySharpContent> GetHomedAtCoreAsync(long homeKey, [EnumeratorCancellation] CancellationToken ct)
	{
		// Home.Reverse carries every object whose home edge points here, including rooms (a room reuses
		// the edge for its drop-to). A drop-to is not a home, so rooms are dropped — same exclusion
		// SurrealDatabase.Objects.cs's GetHomedAtAsync makes.
		var contents = Store.Read(tx => tx.Dups(Tables.Home.Reverse, Keys.Dbref(homeKey))
			.Select(v => Keys.ReadDbref(v))
			.Select(key => ReadObject(tx, key))
			.Where(found => found is not null && found.Value.Record.Type != DatabaseConstants.TypeRoom)
			.Select(found => Hydrate(found!.Value.Dbref, found.Value.Record).AsContent)
			.ToList());

		foreach (var content in contents)
		{
			ct.ThrowIfCancellationRequested();
			yield return content;
		}
	}

	public IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(DBRef obj, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AnySharpObject>(ct => GetNearbyObjectsFromDbRefCoreAsync(obj, ct));

	private async IAsyncEnumerable<AnySharpObject> GetNearbyObjectsFromDbRefCoreAsync(DBRef obj, [EnumeratorCancellation] CancellationToken ct)
	{
		var self = (await GetObjectNodeAsync(obj, ct)).WithoutNone();

		await foreach (var item in GetNearbyObjectsCoreAsync(self, ct))
		{
			yield return item;
		}
	}

	public IAsyncEnumerable<AnySharpObject> GetNearbyObjectsAsync(AnySharpObject obj, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AnySharpObject>(ct => GetNearbyObjectsCoreAsync(obj, ct));

	/// <summary>Self, then the contents of self, then the contents of self's location (self excluded from the
	/// second pass) — the same three-part composition as <c>SurrealDatabase.Navigation.cs</c>'s two
	/// <c>GetNearbyObjectsAsync</c> overloads.</summary>
	private async IAsyncEnumerable<AnySharpObject> GetNearbyObjectsCoreAsync(AnySharpObject obj, [EnumeratorCancellation] CancellationToken ct)
	{
		var location = await obj.Where();

		yield return obj;

		await foreach (var item in GetContentsAsync(obj.Object().DBRef, ct))
		{
			yield return item.WithRoomOption();
		}

		await foreach (var item in GetContentsAsync(location.Object().DBRef, ct))
		{
			if (item.Object().DBRef == obj.Object().DBRef)
			{
				continue;
			}

			yield return item.WithRoomOption();
		}
	}

	public ValueTask<AnyOptionalSharpContainer> GetLocationAsync(DBRef obj, int depth = 1, CancellationToken cancellationToken = default)
	{
		var result = Store.Read<AnyOptionalSharpContainer>(tx =>
		{
			var found = ReadObject(tx, obj.Number);
			if (found is null)
			{
				return new None();
			}

			if (obj.CreationMilliseconds is not null && found.Value.Record.CreationTime != obj.CreationMilliseconds)
			{
				return new None();
			}

			return GetLocationFromKey(tx, found.Value.Dbref, depth);
		});
		return ValueTask.FromResult(result);
	}

	public ValueTask<AnySharpContainer> GetLocationAsync(AnySharpObject obj, int depth = 1, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read(tx => GetLocationFromKey(tx, obj.Object().Key, depth)).WithoutNone());

	public ValueTask<AnySharpContainer> GetLocationAsync(string id, int depth = 1, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read(tx => GetLocationFromKey(tx, ParseDbref(id), depth)).WithoutNone());

	/// <summary>
	/// Walks the location chain from <paramref name="startKey"/>, exactly <paramref name="depth"/> hops
	/// (<c>-1</c> meaning "until there is no further edge", capped at 999 the same as
	/// <c>SurrealDatabase.Navigation.cs</c>'s <c>GetLocationFromTypedIdAsync</c>). A <paramref name="depth"/>
	/// of 0 takes no hops at all, so it returns <see cref="None"/> rather than the starting object itself —
	/// matching that method's actual behavior, not the (looser) doc comment on the interface. If the chain
	/// runs out of edges before <paramref name="depth"/> hops are taken, the last container actually reached
	/// is returned rather than erroring, the same "missing edge mid-walk" tolerance SurrealDB has.
	/// </summary>
	private AnyOptionalSharpContainer GetLocationFromKey(ITx tx, long startKey, int depth)
	{
		var currentKey = startKey;
		var maxHops = depth == -1 ? 999 : depth;
		var hops = 0;
		long? lastValidKey = null;

		while (hops < maxHops)
		{
			var next = GetSingleEdge(tx, Tables.Location.Forward, currentKey);
			if (next is null)
			{
				break;
			}

			lastValidKey = next;
			currentKey = next.Value;
			hops++;
		}

		if (lastValidKey is null)
		{
			return new None();
		}

		var found = ReadObject(tx, lastValidKey.Value);
		if (found is null)
		{
			return new None();
		}

		// .AsContainer throws for an exit, same as the SurrealDB Match's "Invalid Location: Exit" branch —
		// a location chain should never land on an exit, since exits carry no location edge of their own.
		return Hydrate(found.Value.Dbref, found.Value.Record).AsContainer.WithNoneOption();
	}

	public IAsyncEnumerable<AnySharpContent> GetContentsAsync(DBRef obj, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AnySharpContent>(ct => GetContentsCoreAsync(obj.Number, ct));

	public IAsyncEnumerable<AnySharpContent> GetContentsAsync(AnySharpContainer node, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AnySharpContent>(ct => GetContentsCoreAsync(node.Object().Key, ct));

	private async IAsyncEnumerable<AnySharpContent> GetContentsCoreAsync(long containerKey, [EnumeratorCancellation] CancellationToken ct)
	{
		var contents = Store.Read(tx => tx.Dups(Tables.Location.Reverse, Keys.Dbref(containerKey))
			.Select(v => Keys.ReadDbref(v))
			.Select(key => ReadObject(tx, key))
			.Where(found => found is not null && found.Value.Record.Type != DatabaseConstants.TypeRoom)
			.Select(found => Hydrate(found!.Value.Dbref, found.Value.Record).AsContent)
			.ToList());

		foreach (var content in contents)
		{
			ct.ThrowIfCancellationRequested();
			yield return content;
		}
	}

	public IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpExit>(ct => GetExitsCoreAsync(obj.Number, ct));

	public IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpExit>(ct => GetExitsCoreAsync(node.Object().Key, ct));

	private async IAsyncEnumerable<SharpExit> GetExitsCoreAsync(long containerKey, [EnumeratorCancellation] CancellationToken ct)
	{
		// Tables.Exit.Forward is room -> exit directly (set alongside Location when the exit was
		// created), so this is a single point lookup rather than a type-filtered scan of every reverse
		// content entry the way GetContentsCoreAsync above has to be.
		var exits = Store.Read(tx => tx.Dups(Tables.Exit.Forward, Keys.Dbref(containerKey))
			.Select(v => Keys.ReadDbref(v))
			.Select(key => ReadObject(tx, key))
			.Where(found => found is not null)
			.Select(found => Hydrate(found!.Value.Dbref, found.Value.Record).AsExit)
			.ToList());

		foreach (var exit in exits)
		{
			ct.ThrowIfCancellationRequested();
			yield return exit;
		}
	}

	public ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default)
	{
		var objKey = (long)enactorObj.Object().Key;
		var destKey = (long)destination.Object().Key;
		return Store.WriteAsync(tx => SetSingleEdge(tx, Tables.Location, objKey, destKey), cancellationToken);
	}

	public IAsyncEnumerable<SharpObject> GetObjectsByZoneAsync(AnySharpObject zone, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpObject>(ct => GetObjectsByZoneCoreAsync(zone.Object().Key, ct));

	private async IAsyncEnumerable<SharpObject> GetObjectsByZoneCoreAsync(long zoneKey, [EnumeratorCancellation] CancellationToken ct)
	{
		var objects = Store.Read(tx => tx.Dups(Tables.Zone.Reverse, Keys.Dbref(zoneKey))
			.Select(v => Keys.ReadDbref(v))
			.Select(key => ReadObject(tx, key))
			.Where(found => found is not null)
			.Select(found => MapToSharpObject(found!.Value.Dbref, found.Value.Record))
			.ToList());

		foreach (var obj in objects)
		{
			ct.ThrowIfCancellationRequested();
			yield return obj;
		}
	}
}
