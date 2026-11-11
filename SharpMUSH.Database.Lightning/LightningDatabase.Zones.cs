using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// <see cref="IZoneListStore"/>: an object's zones in order. The first is the <see cref="Tables.Zone"/> edge, as it is for
/// an object in one zone; the rest, in the order they were added, are one <see cref="Tables.ZoneMore"/> row, listed
/// under each of them in <see cref="Tables.ZoneMoreReverse"/>. An object has further zones only while it has a first.
/// </summary>
public partial class LightningDatabase : IZoneListStore
{
	public ValueTask<IReadOnlyList<DBRef>> GetZoneRefsAsync(DBRef obj, CancellationToken cancellationToken = default)
		=> ValueTask.FromResult(Store.Read<IReadOnlyList<DBRef>>(tx => IsSubject(tx, obj)
			? ZoneList(tx, obj.Number).Select(zone => HeaderRef(tx, zone)).OfType<DBRef>().ToList()
			: []));

	public async ValueTask SetZonesAsync(DBRef obj, IReadOnlyList<DBRef> zones, CancellationToken cancellationToken = default)
		=> await Store.WriteAsync(tx =>
		{
			if (!IsSubject(tx, obj) || ReadObjectHeader(tx, obj.Number) is null) return;
			SetZoneList(tx, obj.Number, zones.Where(zone => IsSubject(tx, zone) && ReadObjectHeader(tx, zone.Number) is not null)
				.Select(zone => (long)zone.Number).ToList());
		}, cancellationToken);

	/// <summary>The zones <paramref name="dbref"/> is in, its first zone first.</summary>
	internal static List<long> ZoneList(ITx tx, long dbref)
		=> GetSingleEdge(tx, Tables.Zone.Forward, dbref) is { } first ? [first, .. FurtherZones(tx, dbref)] : [];

	/// <summary>Puts <paramref name="dbref"/> in exactly <paramref name="zones"/>, the first as its zone, each once.</summary>
	internal static void SetZoneList(ITx tx, long dbref, IReadOnlyList<long> zones)
	{
		var distinct = zones.Distinct().ToList();
		SetSingleEdge(tx, Tables.Zone, dbref, distinct.Count > 0 ? distinct[0] : null);
		SetFurtherZones(tx, dbref, distinct.Skip(1).ToList());
	}

	/// <summary>
	/// Takes <paramref name="dbref"/> out of every zone list that names it, before the object goes: an object whose first
	/// zone it was moves up to its next. Its own further zones go too. The <see cref="Tables.Zone"/> edges are left to the
	/// delete's sweep of every edge table.
	/// </summary>
	internal static void DeleteZoneListEntries(ITx tx, long dbref)
	{
		var key = Keys.Dbref(dbref);
		var members = tx.Dups(Tables.Zone.Reverse, key)
			.Concat(tx.Dups(Tables.ZoneMoreReverse, key))
			.Select(value => Keys.ReadDbref(value))
			.Where(member => member != dbref)
			.Distinct()
			.ToList();
		foreach (var member in members)
		{
			SetZoneList(tx, member, ZoneList(tx, member).Where(zone => zone != dbref).ToList());
		}

		SetFurtherZones(tx, dbref, []);
	}

	private static List<long> FurtherZones(ITx tx, long dbref)
		=> tx.TryGet(Tables.ZoneMore, Keys.Dbref(dbref), out var value)
			? Enumerable.Range(0, value.Length / 8).Select(i => Keys.ReadDbref(value.AsSpan(i * 8, 8))).ToList()
			: [];

	private static void SetFurtherZones(ITx tx, long dbref, IReadOnlyList<long> further)
	{
		var key = Keys.Dbref(dbref);
		foreach (var old in FurtherZones(tx, dbref))
		{
			tx.Delete(Tables.ZoneMoreReverse, Keys.Dbref(old), key);
		}

		if (further.Count == 0)
		{
			tx.Delete(Tables.ZoneMore, key);
			return;
		}

		tx.Put(Tables.ZoneMore, key, Keys.Concat([.. further.Select(zone => Keys.Dbref(zone))]));
		foreach (var zone in further)
		{
			tx.Put(Tables.ZoneMoreReverse, Keys.Dbref(zone), key);
		}
	}
}
