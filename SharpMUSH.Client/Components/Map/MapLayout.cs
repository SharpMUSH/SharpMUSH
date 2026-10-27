using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Components.Map;

/// <summary>A room where the map draws it: its cell (or pixel, on a graph map), its size and its level.</summary>
/// <param name="Placed">Whether the map says where it is. An unplaced room is drawn in the strip below its level: the level of a room an exit joins it to.</param>
public sealed record MapPlacement(MapRoomDto Room, double X, double Y, double Width, double Height, int Level, bool Placed);

/// <summary>
/// Where each room of a <see cref="MapDto"/> is drawn. No UI state, so the page and the tests share it.
/// <list type="bullet">
/// <item>A coordinate map puts a room at its <c>MAP`COORDS</c>, x to the east and y to the north, one level (z) at a time.</item>
/// <item>A compass map keeps a room's stored <c>MAP`COORDS</c> and places the rest by walking compass exits from them:
/// north is y + 1, up is z + 1. A room no compass exit reaches, or whose cell is taken, is unplaced.</item>
/// <item>A graph map puts a room at its <c>MAP`POS</c>, in the backdrop's pixels, on one level.</item>
/// </list>
/// Screen y grows downward, so a map's y is negated: north is up.
/// </summary>
public sealed class MapLayout
{
	/// <summary>The side of one cell, in drawing units, on a coordinate or compass map.</summary>
	public const double Cell = 96;

	/// <summary>The side of a room with no size on a graph map, in the backdrop's pixels.</summary>
	public const double GraphRoom = 48;

	private static readonly Dictionary<string, (int X, int Y, int Z)> Directions = new(StringComparer.OrdinalIgnoreCase)
	{
		["north"] = (0, 1, 0), ["n"] = (0, 1, 0),
		["south"] = (0, -1, 0), ["s"] = (0, -1, 0),
		["east"] = (1, 0, 0), ["e"] = (1, 0, 0),
		["west"] = (-1, 0, 0), ["w"] = (-1, 0, 0),
		["northeast"] = (1, 1, 0), ["ne"] = (1, 1, 0),
		["northwest"] = (-1, 1, 0), ["nw"] = (-1, 1, 0),
		["southeast"] = (1, -1, 0), ["se"] = (1, -1, 0),
		["southwest"] = (-1, -1, 0), ["sw"] = (-1, -1, 0),
		["up"] = (0, 0, 1), ["u"] = (0, 0, 1),
		["down"] = (0, 0, -1), ["d"] = (0, 0, -1),
	};

	public MapDto Map { get; }

	/// <summary>Every room, placed or not, by dbref.</summary>
	public IReadOnlyDictionary<string, MapPlacement> Rooms { get; }

	/// <summary>The levels that have a placed room, lowest first; 0 alone when none has.</summary>
	public IReadOnlyList<int> Levels { get; }

	public bool IsGraph => Map.Map.Kind == MapKinds.Graph;

	public MapLayout(MapDto map)
	{
		Map = map;
		var placed = map.Map.Kind switch
		{
			MapKinds.Graph => Graph(map),
			MapKinds.Compass => Compass(map),
			_ => Grid(map, map.Rooms),
		};
		var rooms = new Dictionary<string, MapPlacement>(placed);
		Levels = placed.Count == 0 ? [0] : [.. placed.Values.Select(p => p.Level).Distinct().Order()];
		var ground = Levels.Contains(0) ? 0 : Levels[0];
		foreach (var room in map.Rooms.Where(room => !rooms.ContainsKey(room.Id)))
		{
			rooms[room.Id] = new(room, 0, 0, Side(room, 0), Side(room, 1), NeighbourLevel(map, placed, room.Id) ?? ground, Placed: false);
		}
		Rooms = rooms;
	}

	/// <summary>The level of a placed room an exit joins to <paramref name="id"/>, so an unplaced room is drawn beside its neighbours.</summary>
	private static int? NeighbourLevel(MapDto map, Dictionary<string, MapPlacement> placed, string id) =>
		map.Exits
			.Select(exit => exit.Source == id ? exit.Destination : exit.Destination == id ? exit.Source : null)
			.Select(other => other is not null && placed.TryGetValue(other, out var neighbour) ? neighbour.Level : (int?)null)
			.FirstOrDefault(level => level is not null);

	/// <summary>The direction an exit named <paramref name="exit"/> goes, by its name or any alias; null when it names none.</summary>
	public static (int X, int Y, int Z)? Direction(MapExitDto exit) =>
		new[] { exit.Name }.Concat(exit.Aliases)
			.Select(name => Directions.TryGetValue(name.Trim(), out var step) ? step : ((int, int, int)?)null)
			.FirstOrDefault(step => step is not null);

	/// <summary>The rooms placed on <paramref name="level"/>.</summary>
	public IEnumerable<MapPlacement> On(int level) => Rooms.Values.Where(room => room.Placed && room.Level == level);

	/// <summary>The rooms the map does not place on <paramref name="level"/>, in dbref order, laid out in a strip below its rooms.</summary>
	public IReadOnlyList<MapPlacement> Unplaced(int level)
	{
		var bounds = Bounds(level, includeUnplaced: false);
		var unit = IsGraph ? GraphRoom * 1.5 : Cell;
		var top = bounds.Bottom + unit;
		var x = bounds.Left;
		var strip = new List<MapPlacement>();
		foreach (var room in Rooms.Values.Where(room => !room.Placed && room.Level == level).OrderBy(room => Number(room.Room.Id)))
		{
			strip.Add(room with { X = x, Y = top });
			x += room.Width + unit / 4;
		}
		return strip;
	}

	/// <summary>The box around the rooms on <paramref name="level"/>, in drawing units; one cell when there are none.</summary>
	public (double Left, double Top, double Right, double Bottom) Bounds(int level, bool includeUnplaced = true)
	{
		var rooms = On(level).ToList();
		if (includeUnplaced) rooms.AddRange(Unplaced(level));
		if (Map.Size is [var width, var height, ..] && IsGraph) return (0, 0, Math.Max(width, Right(rooms)), Math.Max(height, Bottom(rooms)));
		if (rooms.Count == 0) return (0, 0, IsGraph ? GraphRoom : Cell, IsGraph ? GraphRoom : Cell);
		return (rooms.Min(r => r.X), rooms.Min(r => r.Y), Right(rooms), Bottom(rooms));
	}

	private static double Right(List<MapPlacement> rooms) => rooms.Count == 0 ? 0 : rooms.Max(r => r.X + r.Width);
	private static double Bottom(List<MapPlacement> rooms) => rooms.Count == 0 ? 0 : rooms.Max(r => r.Y + r.Height);

	private double Side(MapRoomDto room, int axis) =>
		(room.Size is { Length: > 1 } size ? size[axis] : 1) * (IsGraph ? (room.Size is null ? GraphRoom : 1) : Cell);

	private Dictionary<string, MapPlacement> Graph(MapDto map)
	{
		var placed = new Dictionary<string, MapPlacement>();
		foreach (var room in map.Rooms)
		{
			if (room.Position is not [var x, var y, ..]) continue;
			var (width, height) = (Side(room, 0), Side(room, 1));
			// A graph room's position is its centre on the picture.
			placed[room.Id] = new(room, x - width / 2, y - height / 2, width, height, 0, Placed: true);
		}
		return placed;
	}

	private Dictionary<string, MapPlacement> Grid(MapDto map, IEnumerable<MapRoomDto> rooms)
	{
		var placed = new Dictionary<string, MapPlacement>();
		foreach (var room in rooms)
		{
			if (room.Position is not [var x, var y, ..] position) continue;
			var z = position.Length > 2 ? (int)Math.Round(position[2]) : 0;
			placed[room.Id] = Cellular(room, x, y, z);
		}
		return placed;
	}

	/// <summary>A room whose south-west cell is (x, y): its top-left corner is the north-west of the area it covers.</summary>
	private MapPlacement Cellular(MapRoomDto room, double x, double y, int z)
	{
		var (width, height) = (Side(room, 0), Side(room, 1));
		return new(room, x * Cell, -(y + 1) * Cell - (height - Cell), width, height, z, Placed: true);
	}

	private Dictionary<string, MapPlacement> Compass(MapDto map)
	{
		var placed = Grid(map, map.Rooms);
		var cells = new Dictionary<(int X, int Y, int Z), string>();
		var cellOf = new Dictionary<string, (int X, int Y, int Z)>();
		foreach (var p in placed.Values.OrderBy(p => Number(p.Room.Id)))
		{
			if (cells.TryAdd(CellOf(p), p.Room.Id)) cellOf[p.Room.Id] = CellOf(p);
		}
		var byId = map.Rooms.ToDictionary(room => room.Id);
		// Each compass exit is a step both ways: north from A to B puts B north of A, and A south of B.
		var steps = new Dictionary<string, List<(string To, (int X, int Y, int Z) Step)>>();
		foreach (var exit in map.Exits.OrderBy(exit => Number(exit.Id)))
		{
			if (exit.Destination is not { } to || !byId.ContainsKey(to) || !byId.ContainsKey(exit.Source)
				|| Direction(exit) is not { } step) continue;
			Steps(exit.Source).Add((to, step));
			Steps(to).Add((exit.Source, (-step.X, -step.Y, -step.Z)));
		}

		var walk = new Queue<string>(cellOf.Keys.OrderBy(Number));
		Walk();
		// A room no walk reached starts one of its own, east of everything drawn so far, so two wings never overlap.
		// The viewer's room goes first, so the part of the map they stand in keeps its shape.
		foreach (var room in map.Rooms.OrderBy(room => room.Id == map.Here ? 0 : 1).ThenBy(room => Number(room.Id)))
		{
			if (cellOf.ContainsKey(room.Id)) continue;
			var cell = (X: cellOf.Count == 0 ? 0 : cellOf.Values.Max(c => c.X) + 2, Y: 0, Z: 0);
			while (cells.ContainsKey(cell)) cell.X++;
			cells[cell] = room.Id;
			cellOf[room.Id] = cell;
			walk.Enqueue(room.Id);
			Walk();
		}

		foreach (var (id, cell) in cellOf.Where(entry => !placed.ContainsKey(entry.Key)))
		{
			placed[id] = Cellular(byId[id], cell.X, cell.Y, cell.Z);
		}
		return placed;

		List<(string To, (int X, int Y, int Z) Step)> Steps(string id) =>
			steps.TryGetValue(id, out var list) ? list : steps[id] = [];

		void Walk()
		{
			while (walk.Count > 0)
			{
				var from = walk.Dequeue();
				var at = cellOf[from];
				foreach (var (to, step) in steps.GetValueOrDefault(from) ?? [])
				{
					var cell = (at.X + step.X, at.Y + step.Y, at.Z + step.Z);
					if (cellOf.ContainsKey(to) || cells.ContainsKey(cell)) continue;
					cells[cell] = to;
					cellOf[to] = cell;
					walk.Enqueue(to);
				}
			}
		}
	}

	private static (int X, int Y, int Z) CellOf(MapPlacement p) =>
		((int)Math.Round(p.Room.Position![0]), (int)Math.Round(p.Room.Position[1]), p.Level);

	private static int Number(string dbref) => int.TryParse(dbref.AsSpan(1), out var number) ? number : int.MaxValue;
}
