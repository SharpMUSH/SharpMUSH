using System.Text.Json;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Plugins.World.Storage;

/// <summary>A room standing on a place of a stored map.</summary>
/// <param name="Map">The map's number.</param>
/// <param name="MapCreated">The map's creation time, so the record never passes to an object that reuses its number.</param>
/// <param name="Created">The room's creation time, for the same reason.</param>
/// <param name="Permanent">Whether it was placed there to stay; otherwise it was lent and goes back once empty.</param>
public sealed record WorldRoomRecord(long Map, long MapCreated, int X, int Y, int Z, long Created, bool Permanent)
{
	/// <summary>Since when a lent room has held nothing, in Unix milliseconds; null while it holds something.</summary>
	public long? EmptySince { get; init; }
}

/// <summary>A room let go of a place, waiting to be tidied: its <c>MAP`PLACE</c> cleared and, when lent, made a spare.</summary>
public sealed record WorldReleaseRecord(long Created, bool Lent);

/// <summary>A spare room, tidied and waiting for any map to borrow it.</summary>
/// <param name="Since">When it became spare, in Unix milliseconds.</param>
public sealed record WorldSpareRecord(long Created, long Since)
{
	/// <summary>The player the room belongs to; it is lent again only on that player's maps.</summary>
	public int Owner { get; init; }
}

/// <summary>What became of placing a room to stay.</summary>
public enum WorldPlacing
{
	/// <summary>It stands there now.</summary>
	Placed,

	/// <summary>It was lent there, and stays now.</summary>
	Kept,

	/// <summary>It was already placed there.</summary>
	AlreadyThere,

	/// <summary>Another room was placed there to stay.</summary>
	Taken,

	/// <summary>The map is no stored map.</summary>
	NotAMap,
}

/// <summary>
/// The rooms standing on stored maps:
/// <list type="table">
///   <item><term><c>world.room</c></term><description>room number → <see cref="WorldRoomRecord"/> JSON.</description></item>
///   <item><term><c>world.standing</c></term><description><see cref="WorldGrid.PlaceKey"/> → the number of the room standing there.</description></item>
///   <item><term><c>world.release</c></term><description>room number → <see cref="WorldReleaseRecord"/> JSON.</description></item>
///   <item><term><c>world.spare</c></term><description>room number → <see cref="WorldSpareRecord"/> JSON.</description></item>
/// </list>
/// The store only keeps the records; changing the rooms themselves is <c>WorldRooms</c>'s.
/// </summary>
public sealed partial class WorldStore
{
	private readonly TableDef _rooms;
	private readonly TableDef _standing;
	private readonly TableDef _releases;
	private readonly TableDef _spares;

	/// <summary>Where <paramref name="room"/> stands; null when on no stored map.</summary>
	public WorldRoomRecord? Room(DBRef room) => _store.Read(tx => RoomIn(tx, room));

	private WorldRoomRecord? RoomIn(ITx tx, DBRef room) =>
		tx.TryGet(_rooms, Keys.Dbref(room.Number), out var bytes)
		&& JsonSerializer.Deserialize<WorldRoomRecord>(bytes, Json) is { } record
		&& (room.CreationMilliseconds is not { } created || record.Created == created)
			? record
			: null;

	/// <summary>The room standing at (x, y, z) of <paramref name="map"/>, with its creation time; null when none does.</summary>
	public DBRef? StandingAt(DBRef map, int x, int y, int z) => _store.Read(tx => StandingIn(tx, map.Number, x, y, z));

	private DBRef? StandingIn(ITx tx, long map, int x, int y, int z)
	{
		if (!tx.TryGet(_standing, WorldGrid.PlaceKey(map, z, x, y), out var bytes)) return null;
		var number = Keys.ReadDbref(bytes);
		return RoomIn(tx, new DBRef((int)number)) is { } record ? new DBRef((int)number, record.Created) : null;
	}

	/// <summary>
	/// Lends <paramref name="room"/> to (x, y, z) of <paramref name="map"/>, unless a room stands there already.
	/// Answers the room standing there afterwards, which is another one when it was. Null when the map is no stored map.
	/// </summary>
	public ValueTask<DBRef?> LendAsync(DBRef map, int x, int y, int z, DBRef room) => _store.WriteAsync(tx =>
	{
		if (MapIn(tx, map) is not { } record) return (DBRef?)null;
		if (StandingIn(tx, map.Number, x, y, z) is { } standing) return standing;
		ForgetRoomIn(tx, room.Number);
		StandIn(tx, room, new WorldRoomRecord(map.Number, record.Created, x, y, z, room.CreationMilliseconds ?? 0, Permanent: false));
		return room;
	});

	/// <summary>
	/// Places <paramref name="room"/> at (x, y, z) of <paramref name="map"/> to stay, from wherever it stood. A room lent
	/// there before goes to be tidied; one placed there to stay is left, and the answer is <see cref="WorldPlacing.Taken"/>.
	/// </summary>
	public ValueTask<WorldPlacing> PlaceRoomAsync(DBRef map, int x, int y, int z, DBRef room) => _store.WriteAsync(tx =>
	{
		if (MapIn(tx, map) is not { } record) return WorldPlacing.NotAMap;
		if (StandingIn(tx, map.Number, x, y, z) is { } standing)
		{
			var there = RoomIn(tx, standing)!;
			if (standing.Number == room.Number && standing.CreationMilliseconds == room.CreationMilliseconds)
			{
				if (there.Permanent) return WorldPlacing.AlreadyThere;
				PutRoom(tx, room.Number, there with { Permanent = true, EmptySince = null });
				return WorldPlacing.Kept;
			}
			if (there.Permanent) return WorldPlacing.Taken;
			ReleaseIn(tx, standing.Number);
		}
		ForgetRoomIn(tx, room.Number);
		StandIn(tx, room, new WorldRoomRecord(map.Number, record.Created, x, y, z, room.CreationMilliseconds ?? 0, Permanent: true));
		return WorldPlacing.Placed;
	});

	/// <summary>
	/// Takes <paramref name="room"/> off the place it stands on. A lent room goes to be tidied when
	/// <paramref name="release"/> says so. Answers where it stood; null when it stood nowhere.
	/// </summary>
	public ValueTask<WorldRoomRecord?> UnstandAsync(DBRef room, bool release) => _store.WriteAsync(tx =>
	{
		if (RoomIn(tx, room) is not { } record) return null;
		if (release) ReleaseIn(tx, room.Number);
		else ForgetRoomIn(tx, room.Number);
		return record;
	});

	/// <summary>Records since when a lent room has been empty, or that it holds something again (null).</summary>
	public ValueTask<bool> SetEmptySinceAsync(DBRef room, long? since) => _store.WriteAsync(tx =>
	{
		if (RoomIn(tx, room) is not { } record) return false;
		PutRoom(tx, room.Number, record with { EmptySince = since });
		return true;
	});

	/// <summary>Every room standing on a stored map, lent or placed, with where it stands.</summary>
	public IReadOnlyList<(DBRef Room, WorldRoomRecord Record)> Rooms() => _store.Read(tx =>
		(IReadOnlyList<(DBRef, WorldRoomRecord)>)
		[
			.. tx.Range(_rooms, [])
				.Select(entry => (Number: Keys.ReadDbref(entry.Key), Record: JsonSerializer.Deserialize<WorldRoomRecord>(entry.Value, Json)!))
				.Select(entry => (new DBRef((int)entry.Number, entry.Record.Created), entry.Record)),
		]);

	/// <summary>The rooms let go of their places and not yet tidied.</summary>
	public IReadOnlyList<(DBRef Room, WorldReleaseRecord Record)> Releases() => _store.Read(tx =>
		(IReadOnlyList<(DBRef, WorldReleaseRecord)>)
		[
			.. tx.Range(_releases, [])
				.Select(entry => (Number: Keys.ReadDbref(entry.Key), Record: JsonSerializer.Deserialize<WorldReleaseRecord>(entry.Value, Json)!))
				.Select(entry => (new DBRef((int)entry.Number, entry.Record.Created), entry.Record)),
		]);

	/// <summary>Forgets a release once the room is tidied.</summary>
	public ValueTask<bool> DropReleaseAsync(DBRef room) => _store.WriteAsync(tx => tx.Delete(_releases, Keys.Dbref(room.Number)));

	/// <summary>The spare rooms, the longest waiting first.</summary>
	public IReadOnlyList<(DBRef Room, WorldSpareRecord Record)> Spares() => _store.Read(tx =>
		(IReadOnlyList<(DBRef, WorldSpareRecord)>)
		[
			.. tx.Range(_spares, [])
				.Select(entry => (Number: Keys.ReadDbref(entry.Key), Record: JsonSerializer.Deserialize<WorldSpareRecord>(entry.Value, Json)!))
				.Select(entry => (new DBRef((int)entry.Number, entry.Record.Created), entry.Record))
				.OrderBy(entry => entry.Record.Since)
				.ThenBy(entry => entry.Item1.Number),
		]);

	/// <summary>Makes <paramref name="room"/>, which <paramref name="owner"/> owns, a spare, as of <paramref name="since"/>.</summary>
	public ValueTask<bool> AddSpareAsync(DBRef room, DBRef owner, long since) => _store.WriteAsync(tx =>
	{
		tx.Put(_spares, Keys.Dbref(room.Number),
			JsonSerializer.SerializeToUtf8Bytes(new WorldSpareRecord(room.CreationMilliseconds ?? 0, since) { Owner = owner.Number }, Json));
		return true;
	});

	/// <summary>Takes the spare of <paramref name="owner"/>'s that has waited longest off the list; null when there is none.</summary>
	public ValueTask<DBRef?> TakeSpareAsync(DBRef owner) => _store.WriteAsync(tx =>
	{
		var first = tx.Range(_spares, [])
			.Select(entry => (Number: Keys.ReadDbref(entry.Key), Record: JsonSerializer.Deserialize<WorldSpareRecord>(entry.Value, Json)!))
			.Where(entry => entry.Record.Owner == owner.Number)
			.OrderBy(entry => entry.Record.Since)
			.ThenBy(entry => entry.Number)
			.Select(entry => (DBRef?)new DBRef((int)entry.Number, entry.Record.Created))
			.FirstOrDefault();
		if (first is { } spare) tx.Delete(_spares, Keys.Dbref(spare.Number));
		return first;
	});

	/// <summary>Takes <paramref name="room"/> off the spare list.</summary>
	public ValueTask<bool> DropSpareAsync(DBRef room) => _store.WriteAsync(tx => tx.Delete(_spares, Keys.Dbref(room.Number)));

	private void StandIn(ITx tx, DBRef room, WorldRoomRecord record)
	{
		PutRoom(tx, room.Number, record);
		tx.Put(_standing, WorldGrid.PlaceKey(record.Map, record.Z, record.X, record.Y), Keys.Dbref(room.Number));
		tx.Delete(_releases, Keys.Dbref(room.Number));
		tx.Delete(_spares, Keys.Dbref(room.Number));
	}

	private void PutRoom(ITx tx, long room, WorldRoomRecord record) =>
		tx.Put(_rooms, Keys.Dbref(room), JsonSerializer.SerializeToUtf8Bytes(record, Json));

	/// <summary>Takes a room off its place and lists it to be tidied.</summary>
	private void ReleaseIn(ITx tx, long room)
	{
		if (!tx.TryGet(_rooms, Keys.Dbref(room), out var bytes)) return;
		var record = JsonSerializer.Deserialize<WorldRoomRecord>(bytes, Json)!;
		ForgetRoomIn(tx, room);
		tx.Put(_releases, Keys.Dbref(room), JsonSerializer.SerializeToUtf8Bytes(new WorldReleaseRecord(record.Created, !record.Permanent), Json));
	}

	/// <summary>Takes a room off its place, with nothing left to do for it.</summary>
	private void ForgetRoomIn(ITx tx, long room)
	{
		if (!tx.TryGet(_rooms, Keys.Dbref(room), out var bytes)) return;
		var record = JsonSerializer.Deserialize<WorldRoomRecord>(bytes, Json)!;
		var place = WorldGrid.PlaceKey(record.Map, record.Z, record.X, record.Y);
		if (tx.TryGet(_standing, place, out var standing) && Keys.ReadDbref(standing) == room) tx.Delete(_standing, place);
		tx.Delete(_rooms, Keys.Dbref(room));
	}

	/// <summary>Lets go of every room standing on map number <paramref name="map"/>.</summary>
	private void ReleaseMapRoomsIn(ITx tx, long map)
	{
		foreach (var (_, room) in tx.Range(_standing, Keys.Dbref(map)).ToList())
		{
			ReleaseIn(tx, Keys.ReadDbref(room));
		}
	}

	/// <summary>Forgets a freed room wherever it stood or waited; a later object with its number is a different room.</summary>
	private ValueTask<bool> ForgetRoomAsync(DBRef room) => _store.WriteAsync(tx =>
	{
		var key = Keys.Dbref(room.Number);
		if (RoomIn(tx, room) is not null) ForgetRoomIn(tx, room.Number);
		if (tx.TryGet(_releases, key, out var release) && Same(JsonSerializer.Deserialize<WorldReleaseRecord>(release, Json)!.Created))
			tx.Delete(_releases, key);
		if (tx.TryGet(_spares, key, out var spare) && Same(JsonSerializer.Deserialize<WorldSpareRecord>(spare, Json)!.Created))
			tx.Delete(_spares, key);
		return true;

		bool Same(long created) => room.CreationMilliseconds is not { } freed || freed == created;
	});
}
