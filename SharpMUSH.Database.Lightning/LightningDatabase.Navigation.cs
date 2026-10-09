using System.Runtime.CompilerServices;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="INavigationStore"/>: graph traversal between objects, over the edge tables declared in <c>LightningDatabase.Objects.cs</c>:
/// <see cref="Tables.Location"/> (object → its container, reverse → contents),
/// <see cref="Tables.Home"/> (object → its home/drop-to/destination, reverse → what homes there),
/// <see cref="Tables.Exit"/> (room → its exits, no reverse walk needed), and
/// <see cref="Tables.Parent"/>/<see cref="Tables.Zone"/> (object → parent/zone, reverse → children/zoned objects).
/// <see cref="IsReachableViaParentOrZoneAsync"/> lives in <c>LightningDatabase.Objects.cs</c> — it is part of
/// <c>IObjectStore</c>, not this store, and sits alongside object deletion.
/// </summary>
public partial class LightningDatabase
{
	public ValueTask<Found<DBRef>> GetRelationRefAsync(ObjectRelationKind relation, DBRef subject,
		CancellationToken cancellationToken = default)
	{
		var forward = relation switch
		{
			ObjectRelationKind.Owner => Tables.Owner.Forward,
			ObjectRelationKind.Parent => Tables.Parent.Forward,
			ObjectRelationKind.Zone => Tables.Zone.Forward,
			// A room's drop-to and an exit's destination reuse the home edge; there is no table of their own.
			ObjectRelationKind.Home => Tables.Home.Forward,
			_ => throw new ArgumentOutOfRangeException(nameof(relation), relation, "Not a single-valued object relation.")
		};

		return ValueTask.FromResult(Store.Read<Found<DBRef>>(tx =>
			IsSubject(tx, subject) && GetSingleEdge(tx, forward, subject.Number) is { } target && HeaderRef(tx, target) is { } found
				? found
				: new NotFound()));
	}

	public IAsyncEnumerable<SharpObject> GetParentsAsync(string id, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpObject>(ct => GetParentsCoreAsync(ParseDbref(id), ct));

	/// <summary>
	/// Walks the parent chain with a visited set keyed
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

	private IAsyncEnumerable<SharpExit> GetEntrancesCoreAsync(long destinationKey, CancellationToken ct)
		=> Store.DupsMapAsync(Tables.Home.Reverse, Keys.Dbref(destinationKey),
			(tx, value) => ReadObject(tx, Keys.ReadDbref(value)) is { } found && found.Record.Type == DatabaseConstants.TypeExit
				? HydrateExit(found.Dbref, found.Record)
				: null,
			ct: ct);

	public IAsyncEnumerable<AnySharpContent> GetHomedAtAsync(DBRef home, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<AnySharpContent>(ct => GetHomedAtCoreAsync(home.Number, ct));

	/// <remarks>Home.Reverse carries every object whose home edge points here, including rooms (a room reuses
	/// the edge for its drop-to). A drop-to is not a home, so rooms are dropped.</remarks>
	private IAsyncEnumerable<AnySharpContent> GetHomedAtCoreAsync(long homeKey, CancellationToken ct)
		=> Store.DupsMapAsync(Tables.Home.Reverse, Keys.Dbref(homeKey), ReadContent, ct: ct);

	/// <summary>The content an edge value names, or null when its object is gone or is a room — the mapping the
	/// contents and homed-at reads share.</summary>
	private AnySharpContent? ReadContent(ITx tx, byte[] value)
		=> ReadObject(tx, Keys.ReadDbref(value)) is { } found && found.Record.Type != DatabaseConstants.TypeRoom
			&& Hydrate(found.Dbref, found.Record).AsOptionalContent is AnySharpContent content
				? content
				: null;

	public ValueTask<Found<DBRef>> GetLocationRefAsync(DBRef subject, int depth = 1, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<Found<DBRef>>(tx =>
			ReadObjectHeader(tx, subject.Number) is { } header
			&& (subject.CreationMilliseconds is null || header.CreationTime == subject.CreationMilliseconds)
			&& GetLocationRefFromKey(tx, subject.Number, depth) is { } location
				? location
				: new NotFound()));

	/// <summary>
	/// Walks the location chain from <paramref name="startKey"/>, exactly <paramref name="depth"/> hops
	/// (<c>-1</c> meaning "until there is no further edge", capped at 999). A <paramref name="depth"/>
	/// of 0 takes no hops at all, so it returns null rather than the starting object itself. If the chain
	/// runs out of edges before <paramref name="depth"/> hops are taken, the last container actually reached
	/// is returned rather than erroring. Null too when that container is gone.
	/// </summary>
	private DBRef? GetLocationRefFromKey(ITx tx, long startKey, int depth)
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

		// The caller resolves the ref through the object node cache, which builds the container once; it is
		// the caller that refuses an exit, which cannot be anyone's container.
		return lastValidKey is { } key ? HeaderRef(tx, key) : null;
	}

	public IAsyncEnumerable<DBRef> GetContentRefsAsync(DBRef container, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<DBRef>(ct => Store.DupsMapValuesAsync(Tables.Location.Reverse, Keys.Dbref(container.Number),
			(tx, value) => IsSubject(tx, container) ? ContentRef(tx, value) : null, ct: ct));

	/// <summary>The full id of the content an edge value names, or null when its object is gone or is a room.</summary>
	private DBRef? ContentRef(ITx tx, byte[] value)
	{
		var dbref = Keys.ReadDbref(value);
		return ReadObjectHeader(tx, dbref) is { } header && header.Type != DatabaseConstants.TypeRoom
			? new DBRef((int)dbref, header.CreationTime)
			: null;
	}

	public IAsyncEnumerable<SharpExit> GetExitsAsync(DBRef obj, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpExit>(ct => GetExitsCoreAsync(obj.Number, ct));

	public IAsyncEnumerable<SharpExit> GetExitsAsync(AnySharpContainer node, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<SharpExit>(ct => GetExitsCoreAsync(node.Object().Key, ct));

	/// <remarks>Tables.Exit.Forward is room -> exit directly (set alongside Location when the exit was
	/// created), so this is a single key's duplicates rather than a type-filtered scan of every reverse
	/// content entry the way <see cref="GetContentRefsAsync"/> has to be.</remarks>
	private IAsyncEnumerable<SharpExit> GetExitsCoreAsync(long containerKey, CancellationToken ct)
		=> Store.DupsMapAsync(Tables.Exit.Forward, Keys.Dbref(containerKey),
			(tx, value) => ReadObject(tx, Keys.ReadDbref(value)) is not { } found
				? null
				: Hydrate(found.Dbref, found.Record) is SharpExit exit
					? exit
					: throw new InvalidOperationException($"#{found.Dbref} is listed as an exit of #{containerKey} but is not one"),
			ct: ct);

	public ValueTask MoveObjectAsync(AnySharpContent enactorObj, AnySharpContainer destination, CancellationToken cancellationToken = default)
		=> WriteContentLocationAsync(enactorObj, destination, cancellationToken);

	private ValueTask WriteContentLocationAsync(AnySharpContent content, AnySharpContainer container, CancellationToken cancellationToken)
	{
		var source = content.Object().DBRef;
		var destination = container.Object().DBRef;
		var sourceType = content.Object().Type;
		var destinationType = container.Object().Type;
		return Store.WriteAsync(tx =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			var stored = ReadObject(tx, source.Number);
			var target = ReadObject(tx, destination.Number);
			if (stored is null || stored.Value.Record.CreationTime != source.CreationMilliseconds
				|| stored.Value.Record.Type != sourceType || sourceType is not (DatabaseConstants.TypePlayer or DatabaseConstants.TypeThing or DatabaseConstants.TypeExit))
				throw new InvalidOperationException($"Content {source} no longer identifies the stored object.");
			if (target is null || target.Value.Record.CreationTime != destination.CreationMilliseconds
				|| target.Value.Record.Type != destinationType || destinationType is not (DatabaseConstants.TypePlayer or DatabaseConstants.TypeThing or DatabaseConstants.TypeRoom))
				throw new InvalidOperationException($"Container {destination} no longer identifies the stored container.");
			if (sourceType == DatabaseConstants.TypeExit)
			{
				// Dispose each bounded duplicate cursor before removing historical source memberships.
				while (true)
				{
					cancellationToken.ThrowIfCancellationRequested();
					var oldSources = tx.Dups(Tables.Exit.Reverse, Keys.Dbref(source.Number)).Take(256).ToArray();
					if (oldSources.Length == 0) break;
					foreach (var old in oldSources)
					{
						cancellationToken.ThrowIfCancellationRequested();
						DeleteEdge(tx, Tables.Exit, Keys.ReadDbref(old), source.Number);
					}
				}
				PutEdge(tx, Tables.Exit, destination.Number, source.Number);
			}
			SetSingleEdge(tx, Tables.Location, source.Number, destination.Number);
			cancellationToken.ThrowIfCancellationRequested();
		}, cancellationToken);
	}

	public IAsyncEnumerable<DBRef> GetZoneMemberRefsAsync(DBRef zone, CancellationToken cancellationToken = default)
		=> new FreshAsyncEnumerable<DBRef>(ct => Store.DupsMapValuesAsync(Tables.Zone.Reverse, Keys.Dbref(zone.Number),
			(tx, value) => IsSubject(tx, zone) ? HeaderRef(tx, Keys.ReadDbref(value)) : null, ct: ct));

	/// <summary>The base object an edge value names, or null when it is gone.</summary>
	private SharpObject? ReadSharpObject(ITx tx, byte[] value)
		=> ReadObject(tx, Keys.ReadDbref(value)) is { } found ? MapToSharpObject(found.Dbref, found.Record) : null;
}
