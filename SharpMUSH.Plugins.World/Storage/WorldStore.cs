using System.Text.Json;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Plugins.World.Storage;

/// <summary>What the store keeps about one stored map.</summary>
/// <param name="Space">The space its places are in; <c>square</c> alone so far.</param>
/// <param name="Created">The map object's creation time, so a record never passes to an object that reuses its number.</param>
/// <param name="Palette">The terrain types painted on it, by terrain id less one.</param>
public sealed record WorldMapRecord(string Space, long Created, List<string> Palette)
{
	/// <summary>Whether anything has been painted, and so whether the box below means anything.</summary>
	public bool Painted { get; init; }

	public int Left { get; init; }
	public int Bottom { get; init; }
	public int Right { get; init; }
	public int Top { get; init; }

	/// <summary>The levels anything has been painted on, lowest first.</summary>
	public List<int> Levels { get; init; } = [];

	/// <summary>The terrain type a terrain id stands for; null for 0 or an id the palette does not have.</summary>
	public string? Type(ushort terrain) => terrain > 0 && terrain <= Palette.Count ? Palette[terrain - 1] : null;
}

/// <summary>What a place has beyond its terrain and edges.</summary>
/// <param name="Name">Its own name.</param>
/// <param name="Data">A JSON object of whatever the game keeps there.</param>
/// <param name="Reasons">Why each edge it owns refuses a mover, by side letter (<c>n</c>, <c>e</c>, <c>u</c>).</param>
public sealed record WorldPlaceRecord(string? Name, string? Data, Dictionary<string, string>? Reasons)
{
	public static readonly WorldPlaceRecord Empty = new(null, null, null);

	/// <summary>The lock key of each locked edge it owns, by side letter, bound to objids as <c>@lock</c> stores one.</summary>
	public Dictionary<string, string>? Keys { get; init; }

	public bool IsEmpty => Name is null && Data is null && Reasons is not { Count: > 0 } && Keys is not { Count: > 0 };
}

/// <summary>One place read out of the store: its terrain id and the edges it owns.</summary>
public readonly record struct WorldCell(ushort Terrain, ushort Edges)
{
	public bool IsEmpty => Terrain == 0 && Edges == 0;
}

/// <summary>A window of a stored map read at once: its record, its places' cells, and their records.</summary>
/// <param name="Cells">Every place with a terrain or an edge in the window, and the row below and column left of it.</param>
public sealed record WorldWindowRead(WorldMapRecord Map, IReadOnlyDictionary<(int X, int Y), WorldCell> Cells,
	IReadOnlyDictionary<(int X, int Y), WorldPlaceRecord> Places);

/// <summary>
/// The World plugin's store, over the host-shared Lightning accessor. Three tables for the places:
/// <list type="table">
///   <item><term><c>world.map</c></term><description>map number → <see cref="WorldMapRecord"/> JSON.</description></item>
///   <item><term><c>world.tile</c></term><description><see cref="WorldGrid.TileKey"/> → 32 by 32 cells (<see cref="WorldGrid"/>).</description></item>
///   <item><term><c>world.place</c></term><description><see cref="WorldGrid.PlaceKey"/> → <see cref="WorldPlaceRecord"/> JSON.</description></item>
/// </list>
/// and four for the rooms standing on them, in <c>WorldStore.Rooms.cs</c>.
/// Each change is one write job, which does its own reads through the transaction it is handed; tables are opened
/// once, here, never inside a job.
/// </summary>
public sealed partial class WorldStore : IFreedObjectCleanup
{
	private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

	/// <summary>The most places one paint may cover.</summary>
	public const long MaxPaint = 512 * 512;

	private readonly ILightningStorageAccessor _store;
	private readonly TableDef _maps;
	private readonly TableDef _tiles;
	private readonly TableDef _places;

	public WorldStore(ILightningStorageAccessor store)
	{
		_store = store;
		_maps = store.OpenTable("world.map", duplicates: false);
		_tiles = store.OpenTable("world.tile", duplicates: false);
		_places = store.OpenTable("world.place", duplicates: false);
		_rooms = store.OpenTable("world.room", duplicates: false);
		_standing = store.OpenTable("world.standing", duplicates: false);
		_releases = store.OpenTable("world.release", duplicates: false);
		_spares = store.OpenTable("world.spare", duplicates: false);
	}

	/// <summary>The record of <paramref name="map"/>, when it is a stored map; null otherwise.</summary>
	public WorldMapRecord? Map(DBRef map) => _store.Read(tx => MapIn(tx, map));

	private WorldMapRecord? MapIn(ITx tx, DBRef map) =>
		tx.TryGet(_maps, Keys.Dbref(map.Number), out var bytes)
		&& JsonSerializer.Deserialize<WorldMapRecord>(bytes, Json) is { } record
		&& (map.CreationMilliseconds is not { } created || record.Created == created)
			? record
			: null;

	/// <summary>
	/// Makes <paramref name="map"/> a stored map in <paramref name="space"/>. False when it already is one. A record
	/// left by an earlier object with the same number is dropped first.
	/// </summary>
	public ValueTask<bool> CreateAsync(DBRef map, string space) => _store.WriteAsync(tx =>
	{
		if (MapIn(tx, map) is not null) return false;
		DeleteIn(tx, map.Number);
		Put(tx, map.Number, new WorldMapRecord(space, map.CreationMilliseconds ?? 0, []));
		return true;
	});

	private void Put(ITx tx, long map, WorldMapRecord record) =>
		tx.Put(_maps, Keys.Dbref(map), JsonSerializer.SerializeToUtf8Bytes(record, Json));

	/// <summary>
	/// Paints every place from (x1, y1) to (x2, y2) on <paramref name="level"/> as <paramref name="type"/>, or erases
	/// their terrain when it is null. Edges stay. Answers how many places it covered, or null when
	/// <paramref name="map"/> is no stored map or the palette is full.
	/// </summary>
	public ValueTask<long?> PaintAsync(DBRef map, int level, int x1, int y1, int x2, int y2, string? type) => _store.WriteAsync(tx =>
	{
		if (MapIn(tx, map) is not { } record) return (long?)null;
		var (left, right) = (Math.Min(x1, x2), Math.Max(x1, x2));
		var (bottom, top) = (Math.Min(y1, y2), Math.Max(y1, y2));
		ushort terrain = 0;
		if (type is not null)
		{
			var index = record.Palette.IndexOf(type);
			if (index < 0)
			{
				if (record.Palette.Count >= ushort.MaxValue) return null;
				record = record with { Palette = [.. record.Palette, type] };
				index = record.Palette.Count - 1;
			}
			terrain = (ushort)(index + 1);
		}

		var (fromTileX, _) = WorldGrid.Split(left);
		var (toTileX, _) = WorldGrid.Split(right);
		var (fromTileY, _) = WorldGrid.Split(bottom);
		var (toTileY, _) = WorldGrid.Split(top);
		for (var tileX = fromTileX; tileX <= toTileX; tileX++)
		{
			for (var tileY = fromTileY; tileY <= toTileY; tileY++)
			{
				var key = WorldGrid.TileKey(map.Number, level, tileX, tileY);
				var tile = tx.TryGet(_tiles, key, out var stored) ? stored : new byte[WorldGrid.TileBytes];
				for (var column = 0; column < WorldGrid.TileSide; column++)
				{
					var x = tileX * WorldGrid.TileSide + column;
					if (x < left || x > right) continue;
					for (var row = 0; row < WorldGrid.TileSide; row++)
					{
						var y = tileY * WorldGrid.TileSide + row;
						if (y >= bottom && y <= top) WorldGrid.SetTerrain(tile, column, row, terrain);
					}
				}
				PutTile(tx, key, tile);
			}
		}

		if (type is null) record = Extent(tx, map.Number, record);
		else
		{
			record = record.Painted
				? record with
				{
					Left = Math.Min(record.Left, left),
					Bottom = Math.Min(record.Bottom, bottom),
					Right = Math.Max(record.Right, right),
					Top = Math.Max(record.Top, top),
				}
				: record with { Painted = true, Left = left, Bottom = bottom, Right = right, Top = top };
			if (!record.Levels.Contains(level)) record = record with { Levels = [.. record.Levels.Append(level).Order()] };
		}
		Put(tx, map.Number, record);
		return ((long)right - left + 1) * ((long)top - bottom + 1);
	});

	/// <summary>
	/// <paramref name="record"/> with the box and levels of what is still painted, read from every tile of the map, after
	/// an erase that may have taken the outermost places or a whole level.
	/// </summary>
	private WorldMapRecord Extent(ITx tx, long map, WorldMapRecord record)
	{
		var (left, bottom, right, top) = (int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);
		var levels = new SortedSet<int>();
		foreach (var (key, tile) in tx.Range(_tiles, Keys.Dbref(map)))
		{
			var (level, tileX, tileY) = WorldGrid.ReadTileKey(key);
			for (var column = 0; column < WorldGrid.TileSide; column++)
			{
				for (var row = 0; row < WorldGrid.TileSide; row++)
				{
					if (WorldGrid.Terrain(tile, column, row) == 0) continue;
					var (x, y) = (tileX * WorldGrid.TileSide + column, tileY * WorldGrid.TileSide + row);
					(left, bottom, right, top) = (Math.Min(left, x), Math.Min(bottom, y), Math.Max(right, x), Math.Max(top, y));
					levels.Add(level);
				}
			}
		}
		return levels.Count == 0
			? record with { Painted = false, Left = 0, Bottom = 0, Right = 0, Top = 0, Levels = [] }
			: record with { Painted = true, Left = left, Bottom = bottom, Right = right, Top = top, Levels = [.. levels] };
	}

	/// <summary>Stores a tile, or drops it when no place in it has a terrain or an edge.</summary>
	private void PutTile(ITx tx, byte[] key, byte[] tile)
	{
		if (tile.AsSpan().ContainsAnyExcept((byte)0)) tx.Put(_tiles, key, tile);
		else tx.Delete(_tiles, key);
	}

	/// <summary>The place at (x, y, z) of <paramref name="map"/>; empty when nothing is there.</summary>
	public WorldCell Cell(DBRef map, int x, int y, int z) => _store.Read(tx => CellIn(tx, map.Number, x, y, z));

	private WorldCell CellIn(ITx tx, long map, int x, int y, int z)
	{
		var (tileX, column) = WorldGrid.Split(x);
		var (tileY, row) = WorldGrid.Split(y);
		return tx.TryGet(_tiles, WorldGrid.TileKey(map, z, tileX, tileY), out var tile)
			? new WorldCell(WorldGrid.Terrain(tile, column, row), WorldGrid.Edges(tile, column, row))
			: default;
	}

	/// <summary>
	/// The edge on <paramref name="side"/> of (x, y, z), as that place sees it, why it refuses a mover, and the key a
	/// locked edge opens for.
	/// </summary>
	public (WorldEdge Edge, string? Reason, string? Key) Edge(DBRef map, int x, int y, int z, WorldSide side) => _store.Read(tx =>
	{
		var (ox, oy, oz, owned, flipped) = WorldGrid.Owner(x, y, z, side);
		var edge = WorldGrid.Edge(CellIn(tx, map.Number, ox, oy, oz).Edges, owned);
		var place = PlaceIn(tx, map.Number, ox, oy, oz);
		var reason = place?.Reasons?.GetValueOrDefault(WorldGrid.Letter(owned));
		var key = edge == WorldEdge.Lock ? place?.Keys?.GetValueOrDefault(WorldGrid.Letter(owned)) : null;
		return (flipped ? WorldGrid.Flip(edge) : edge, edge == WorldEdge.Open ? null : reason, key);
	});

	/// <summary>
	/// Sets the edge on <paramref name="side"/> of (x, y, z), as that place sees it, and why it refuses a mover (null
	/// for no reason). A locked edge keeps its key; any other loses it. False when <paramref name="map"/> is no stored map.
	/// </summary>
	public ValueTask<bool> SetEdgeAsync(DBRef map, int x, int y, int z, WorldSide side, WorldEdge edge, string? reason) =>
		_store.WriteAsync(tx => SetEdgeIn(tx, map, x, y, z, side, edge, (place, letter) =>
		{
			var reasons = new Dictionary<string, string>(place.Reasons ?? []);
			if (edge == WorldEdge.Open || string.IsNullOrWhiteSpace(reason)) reasons.Remove(letter);
			else reasons[letter] = reason.Trim();
			var keys = new Dictionary<string, string>(place.Keys ?? []);
			if (edge != WorldEdge.Lock) keys.Remove(letter);
			return place with { Reasons = reasons.Count > 0 ? reasons : null, Keys = keys.Count > 0 ? keys : null };
		}));

	/// <summary>
	/// Locks the edge on <paramref name="side"/> of (x, y, z) with <paramref name="key"/>, keeping its reason; a null key
	/// opens it. False when <paramref name="map"/> is no stored map.
	/// </summary>
	public ValueTask<bool> SetLockAsync(DBRef map, int x, int y, int z, WorldSide side, string? key) =>
		_store.WriteAsync(tx => SetEdgeIn(tx, map, x, y, z, side, key is null ? WorldEdge.Open : WorldEdge.Lock, (place, letter) =>
		{
			var reasons = new Dictionary<string, string>(place.Reasons ?? []);
			var keys = new Dictionary<string, string>(place.Keys ?? []);
			if (key is null)
			{
				reasons.Remove(letter);
				keys.Remove(letter);
			}
			else keys[letter] = key;
			return place with { Reasons = reasons.Count > 0 ? reasons : null, Keys = keys.Count > 0 ? keys : null };
		}));

	/// <summary>
	/// Stores <paramref name="edge"/> on the place that owns the edge on <paramref name="side"/> of (x, y, z), and that
	/// place's record as <paramref name="change"/> leaves it, handed the record and the owned edge's letter.
	/// </summary>
	private bool SetEdgeIn(ITx tx, DBRef map, int x, int y, int z, WorldSide side, WorldEdge edge,
		Func<WorldPlaceRecord, string, WorldPlaceRecord> change)
	{
		if (MapIn(tx, map) is null) return false;
		var (ox, oy, oz, owned, flipped) = WorldGrid.Owner(x, y, z, side);
		var (tileX, column) = WorldGrid.Split(ox);
		var (tileY, row) = WorldGrid.Split(oy);
		var key = WorldGrid.TileKey(map.Number, oz, tileX, tileY);
		var tile = tx.TryGet(_tiles, key, out var stored) ? stored : new byte[WorldGrid.TileBytes];
		var stores = flipped ? WorldGrid.Flip(edge) : edge;
		WorldGrid.SetEdges(tile, column, row, WorldGrid.WithEdge(WorldGrid.Edges(tile, column, row), owned, stores));
		PutTile(tx, key, tile);
		PutPlace(tx, map.Number, ox, oy, oz, change(PlaceIn(tx, map.Number, ox, oy, oz) ?? WorldPlaceRecord.Empty, WorldGrid.Letter(owned)));
		return true;
	}

	/// <summary>The record of the place at (x, y, z); null when it has none.</summary>
	public WorldPlaceRecord? Place(DBRef map, int x, int y, int z) => _store.Read(tx => PlaceIn(tx, map.Number, x, y, z));

	private WorldPlaceRecord? PlaceIn(ITx tx, long map, int x, int y, int z) =>
		tx.TryGet(_places, WorldGrid.PlaceKey(map, z, x, y), out var bytes)
			? JsonSerializer.Deserialize<WorldPlaceRecord>(bytes, Json)
			: null;

	private void PutPlace(ITx tx, long map, int x, int y, int z, WorldPlaceRecord place)
	{
		var key = WorldGrid.PlaceKey(map, z, x, y);
		if (place.IsEmpty) tx.Delete(_places, key);
		else tx.Put(_places, key, JsonSerializer.SerializeToUtf8Bytes(place, Json));
	}

	/// <summary>
	/// Changes the record of the place at (x, y, z) with <paramref name="change"/>, which sees the record as it stands
	/// (empty when there is none). A record left empty is removed. False when <paramref name="map"/> is no stored map.
	/// </summary>
	public ValueTask<bool> ChangePlaceAsync(DBRef map, int x, int y, int z, Func<WorldPlaceRecord, WorldPlaceRecord> change) =>
		_store.WriteAsync(tx =>
		{
			if (MapIn(tx, map) is null) return false;
			PutPlace(tx, map.Number, x, y, z, change(PlaceIn(tx, map.Number, x, y, z) ?? WorldPlaceRecord.Empty));
			return true;
		});

	/// <summary>
	/// The window from (left, bottom) to (right, top) of <paramref name="level"/>: its cells, with the row below and
	/// the column left of it (whose edges border the window), and its place records with a border of one all round (the
	/// places those edges lead to). Null for no stored map.
	/// </summary>
	public WorldWindowRead? Window(DBRef map, int level, int left, int bottom, int right, int top) => _store.Read(tx =>
	{
		if (MapIn(tx, map) is not { } record) return null;
		// Nothing is stored past reach, and clamping to it keeps every bound below and the column loop clear of overflow.
		(left, bottom) = (Math.Max(left, -WorldGrid.Reach), Math.Max(bottom, -WorldGrid.Reach));
		(right, top) = (Math.Min(right, WorldGrid.Reach), Math.Min(top, WorldGrid.Reach));
		if (left > right || bottom > top || Math.Abs((long)level) > WorldGrid.Reach)
			return new WorldWindowRead(record, new Dictionary<(int, int), WorldCell>(), new Dictionary<(int, int), WorldPlaceRecord>());
		var cells = new Dictionary<(int, int), WorldCell>();
		var (fromTileX, _) = WorldGrid.Split(left - 1);
		var (toTileX, _) = WorldGrid.Split(right);
		var (fromTileY, _) = WorldGrid.Split(bottom - 1);
		var (toTileY, _) = WorldGrid.Split(top);
		for (var tileX = fromTileX; tileX <= toTileX; tileX++)
		{
			for (var tileY = fromTileY; tileY <= toTileY; tileY++)
			{
				if (!tx.TryGet(_tiles, WorldGrid.TileKey(map.Number, level, tileX, tileY), out var tile)) continue;
				for (var column = 0; column < WorldGrid.TileSide; column++)
				{
					var x = tileX * WorldGrid.TileSide + column;
					if (x < left - 1 || x > right) continue;
					for (var row = 0; row < WorldGrid.TileSide; row++)
					{
						var y = tileY * WorldGrid.TileSide + row;
						if (y < bottom - 1 || y > top) continue;
						var cell = new WorldCell(WorldGrid.Terrain(tile, column, row), WorldGrid.Edges(tile, column, row));
						if (!cell.IsEmpty) cells[(x, y)] = cell;
					}
				}
			}
		}

		var places = new Dictionary<(int, int), WorldPlaceRecord>();
		for (var x = left - 1; x <= right + 1; x++)
		{
			foreach (var (key, value) in tx.Range(_places, WorldGrid.PlaceColumn(map.Number, level, x)))
			{
				var (_, _, y) = WorldGrid.ReadPlaceKey(key);
				if (y >= bottom - 1 && y <= top + 1 && JsonSerializer.Deserialize<WorldPlaceRecord>(value, Json) is { } place)
					places[(x, y)] = place;
			}
		}
		return new WorldWindowRead(record, cells, places);
	});

	/// <summary>Forgets everything stored for map number <paramref name="map"/>.</summary>
	public ValueTask DeleteAsync(long map) => new(_store.WriteAsync(tx =>
	{
		DeleteIn(tx, map);
		return true;
	}).AsTask());

	private void DeleteIn(ITx tx, long map)
	{
		ReleaseMapRoomsIn(tx, map);
		tx.Delete(_maps, Keys.Dbref(map));
		tx.DeletePrefix(_tiles, Keys.Dbref(map));
		tx.DeletePrefix(_places, Keys.Dbref(map));
	}

	/// <summary>
	/// A freed map's places go with it, so a new object with its number starts with none, and its rooms are let go.
	/// A freed room is forgotten wherever it stood or waited.
	/// </summary>
	public async ValueTask ObjectsFreedAsync(IReadOnlyCollection<DBRef> objids, CancellationToken cancellationToken = default)
	{
		foreach (var objid in objids)
		{
			if (Map(objid) is not null) await DeleteAsync(objid.Number);
			await ForgetRoomAsync(objid);
		}
	}
}
