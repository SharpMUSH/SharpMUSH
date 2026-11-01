using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Components.Map;

/// <summary>A room where the map draws it: its cell (or pixel, on a graph map), its size and its level.</summary>
/// <param name="Placed">Whether the map says where it is. An unplaced room is drawn in the strip below its level: the level of a room an exit joins it to.</param>
/// <param name="Region">A map inside this one rather than a room: choosing it opens that map.</param>
/// <param name="Outside">A room whose stored point or size reaches past its map's picture: it is drawn at the picture's edge.</param>
public sealed record MapPlacement(MapRoomDto Room, double X, double Y, double Width, double Height, int Level, bool Placed,
	bool Region = false, bool Outside = false);

/// <summary>
/// Where a room goes after a move or a resize, in the map's own units (see <see cref="MapLayout.Spot"/>), and its size
/// when that changed: cells on a grid, pixels on a picture.
/// </summary>
/// <param name="AtEdge">The picture's edge stopped it short of where it was taken.</param>
public sealed record MapReshape(double X, double Y, int Z, double? Width = null, double? Height = null, bool AtEdge = false);

/// <summary>
/// Where each room of a <see cref="MapDto"/> is drawn. No UI state, so the page and the tests share it.
/// <list type="bullet">
/// <item>A coordinate map puts a room at its <c>MAP`COORDS</c>, x to the east and y to the north, one level (z) at a time.</item>
/// <item>A compass map keeps a room's stored <c>MAP`COORDS</c> and places the rest by walking compass exits from them:
/// north is y + 1, up is z + 1. A room no compass exit reaches, or whose cell is taken, is unplaced.</item>
/// <item>A graph map puts a room at its <c>MAP`POS</c>, in the backdrop's pixels, on one level.</item>
/// </list>
/// A region (a map zoned to this one) is placed as a room is on a coordinate or graph map; on a compass map only by its
/// stored <c>MAP`COORDS</c>, since no exit leads to it.
/// Screen y grows downward, so a map's y is negated: north is up.
/// A hex world map's places are flat-topped hexagons at axial x (q) and y (r): column x is 1.5 sides east of column
/// x - 1 and half a row south of it, so the place at (x, y) is centred at (1.5·x·s, -(y + x/2)·√3·s). A pointy hex
/// map's are pointy-topped: row y is 1.5 sides north of row y - 1 and half a place east of it, centred at
/// (√3·s·(x + y/2), -1.5·y·s). Its rooms stand on their places, one hexagon each.
/// </summary>
public sealed class MapLayout
{
	/// <summary>The side of one cell, in drawing units, on a coordinate or compass map.</summary>
	public const double Cell = 96;

	/// <summary>How far in from its box a room is drawn, as the map canvas draws it.</summary>
	public const double RoomInset = 6;

	/// <summary>The side of a hexagon on a hex world map, in drawing units: a hexagon is <see cref="Cell"/> wide.</summary>
	public const double HexSide = Cell / 2;

	/// <summary>The height of a hexagon on a hex world map, from its north side to its south side.</summary>
	public static readonly double HexHeight = Math.Sqrt(3) * HexSide;

	/// <summary>The side of a room with no size on a graph map, in the backdrop's pixels: a grid cell's, so its name fits.</summary>
	public const double GraphRoom = Cell;

	/// <summary>The least side a room on a graph map is drawn with, whatever its size says, so it can be seen and chosen.</summary>
	public const double MinGraphRoom = 4 * RoomInset;

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

	/// <summary>
	/// The steps on a flat-topped hex map, in its axial coordinates; east and west go round a corner to the hex two over.
	/// </summary>
	private static readonly Dictionary<string, (int X, int Y, int Z)> HexDirections = new(StringComparer.OrdinalIgnoreCase)
	{
		["north"] = (0, 1, 0), ["n"] = (0, 1, 0),
		["south"] = (0, -1, 0), ["s"] = (0, -1, 0),
		["east"] = (2, -1, 0), ["e"] = (2, -1, 0),
		["west"] = (-2, 1, 0), ["w"] = (-2, 1, 0),
		["northeast"] = (1, 0, 0), ["ne"] = (1, 0, 0),
		["northwest"] = (-1, 1, 0), ["nw"] = (-1, 1, 0),
		["southeast"] = (1, -1, 0), ["se"] = (1, -1, 0),
		["southwest"] = (-1, 0, 0), ["sw"] = (-1, 0, 0),
		["up"] = (0, 0, 1), ["u"] = (0, 0, 1),
		["down"] = (0, 0, -1), ["d"] = (0, 0, -1),
	};

	/// <summary>
	/// The steps on a pointy-topped hex map, in its axial coordinates; north and south go round a corner to the hex two over.
	/// </summary>
	private static readonly Dictionary<string, (int X, int Y, int Z)> PointyHexDirections = new(StringComparer.OrdinalIgnoreCase)
	{
		["north"] = (-1, 2, 0), ["n"] = (-1, 2, 0),
		["south"] = (1, -2, 0), ["s"] = (1, -2, 0),
		["east"] = (1, 0, 0), ["e"] = (1, 0, 0),
		["west"] = (-1, 0, 0), ["w"] = (-1, 0, 0),
		["northeast"] = (0, 1, 0), ["ne"] = (0, 1, 0),
		["northwest"] = (-1, 1, 0), ["nw"] = (-1, 1, 0),
		["southeast"] = (1, -1, 0), ["se"] = (1, -1, 0),
		["southwest"] = (0, -1, 0), ["sw"] = (0, -1, 0),
		["up"] = (0, 0, 1), ["u"] = (0, 0, 1),
		["down"] = (0, 0, -1), ["d"] = (0, 0, -1),
	};

	public MapDto Map { get; }

	/// <summary>Every room and region, placed or not, by dbref.</summary>
	public IReadOnlyDictionary<string, MapPlacement> Rooms { get; }

	/// <summary>The levels that have a placed room or a stored place, lowest first; 0 alone when none has.</summary>
	public IReadOnlyList<int> Levels { get; }

	public bool IsGraph => Map.Map.Kind == MapKinds.Graph;

	/// <summary>
	/// The square or hex cells a map source stores this map's places in, drawn under its rooms; null for a map of rooms
	/// alone, a picture, or a space this portal does not draw.
	/// </summary>
	public MapSpaceDto? Space =>
		!IsGraph && Map.Space is { Kind: MapSpaces.Square or MapSpaces.Hex or MapSpaces.PointyHex or MapSpaces.Graph } space ? space : null;

	/// <summary>Whether the map's places are hexagons, flat- or pointy-topped.</summary>
	public bool IsHex => MapSpaces.IsHex(Space?.Kind);

	/// <summary>Whether the map's places are pointy-topped hexagons.</summary>
	public bool IsPointy => Space is { Kind: MapSpaces.PointyHex };

	/// <summary>
	/// Whether the map's places are joined by the named ways made between them, not by their sides: a graph world map,
	/// whose places stand in square cells with no grid drawn.
	/// </summary>
	public bool HasWays => Space is { Kind: MapSpaces.Graph };

	/// <summary>
	/// The picture a graph map's rooms stand on, in its pixels: every room is drawn inside it, and a move or resize stays
	/// inside it. Null for a graph map with no size, which has no edge, and for every other kind.
	/// </summary>
	public (double Width, double Height)? Picture => IsGraph && Map.Size is [> 0 and var width, > 0 and var height, ..] ? (width, height) : null;

	/// <summary>How far one arrow key moves a room on a picture, in its pixels; Shift moves five times as far.</summary>
	public const double PictureStep = 8;

	/// <summary>The width of the map's hexagons, from corner to corner when flat-topped and side to side when pointy.</summary>
	private double HexWidth => IsPointy ? HexHeight : 2 * HexSide;

	/// <summary>The height of the map's hexagons, from side to side when flat-topped and corner to corner when pointy.</summary>
	private double HexTall => IsPointy ? 2 * HexSide : HexHeight;

	public MapLayout(MapDto map)
	{
		Map = map;
		var placed = map.Map.Kind switch
		{
			MapKinds.Graph => Graph(map.Rooms),
			MapKinds.Compass => Compass(map),
			_ => Grid(map.Rooms),
		};
		foreach (var (id, region) in IsGraph ? Graph(map.Regions) : Grid(map.Regions)) placed.TryAdd(id, region with { Region = true });
		var rooms = new Dictionary<string, MapPlacement>(placed);
		var levels = placed.Values.Select(p => p.Level).Concat(Space?.Levels ?? []).Distinct().Order().ToList();
		Levels = levels.Count == 0 ? [0] : levels;
		var ground = Levels.Contains(0) ? 0 : Levels[0];
		foreach (var room in map.Rooms.Where(room => !rooms.ContainsKey(room.Id)))
		{
			rooms[room.Id] = new(room, 0, 0, Side(room, 0), Side(room, 1), NeighbourLevel(map, placed, room.Id) ?? ground, Placed: false);
		}
		foreach (var region in map.Regions.Where(region => !rooms.ContainsKey(region.Id)))
		{
			rooms[region.Id] = new(region, 0, 0, Side(region, 0), Side(region, 1), ground, Placed: false, Region: true);
		}
		Rooms = rooms;
	}

	/// <summary>The level of a placed room an exit joins to <paramref name="id"/>, so an unplaced room is drawn beside its neighbours.</summary>
	private static int? NeighbourLevel(MapDto map, Dictionary<string, MapPlacement> placed, string id) =>
		map.Exits
			.Select(exit => exit.Source == id ? exit.Destination : exit.Destination == id ? exit.Source : null)
			.Select(other => other is not null && placed.TryGetValue(other, out var neighbour) ? neighbour.Level : (int?)null)
			.FirstOrDefault(level => level is not null);

	/// <summary>
	/// The direction an exit named <paramref name="exit"/> goes, by its name or any alias, in the cells of a map whose space
	/// is <paramref name="space"/>: a square grid's unless it names a hex one; null when it names none.
	/// </summary>
	public static (int X, int Y, int Z)? Direction(MapExitDto exit, string? space = null)
	{
		var steps = space switch
		{
			MapSpaces.Hex => HexDirections,
			MapSpaces.PointyHex => PointyHexDirections,
			_ => Directions,
		};
		return new[] { exit.Name }.Concat(exit.Aliases)
			.Select(name => steps.TryGetValue(name.Trim(), out var step) ? step : ((int, int, int)?)null)
			.FirstOrDefault(step => step is not null);
	}

	/// <summary>The rooms placed on <paramref name="level"/>.</summary>
	public IEnumerable<MapPlacement> On(int level) => Rooms.Values.Where(room => room.Placed && room.Level == level)
		// Larger first, so a room standing inside another is drawn over it, not hidden under it.
		.OrderByDescending(room => room.Width * room.Height);

	/// <summary>
	/// Where <paramref name="room"/> is in the map's own units: its south-west cell and level on a coordinate or compass
	/// map, its centre on a graph map. A room the map does not place is at (0, 0) on its level.
	/// </summary>
	public (double X, double Y, int Z) Spot(MapPlacement room) => room switch
	{
		{ Placed: false } => (0, 0, room.Level),
		_ when IsGraph => (room.X + room.Width / 2, room.Y + room.Height / 2, room.Level),
		_ when IsHex => HexSpot(room),
		_ => (Math.Round(room.X / Cell), Math.Round(-(room.Y + room.Height) / Cell), room.Level),
	};

	private (double X, double Y, int Z) HexSpot(MapPlacement room)
	{
		var (x, y) = PlaceAt(room.X + room.Width / 2, room.Y + room.Height / 2);
		return (x, y, room.Level);
	}

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

	/// <summary>
	/// The box around the rooms on <paramref name="level"/> and, with <paramref name="includePlaces"/>, the stored places
	/// of every level, in drawing units; one cell when there are none.
	/// </summary>
	public (double Left, double Top, double Right, double Bottom) Bounds(int level, bool includeUnplaced = true, bool includePlaces = true)
	{
		var rooms = On(level).ToList();
		if (includeUnplaced) rooms.AddRange(Unplaced(level));
		if (Map.Size is [var width, var height, ..] && IsGraph) return (0, 0, Math.Max(width, Right(rooms)), Math.Max(height, Bottom(rooms)));
		if (rooms.Count == 0 && (Space is null || !includePlaces)) return (0, 0, IsGraph ? GraphRoom : Cell, IsGraph ? GraphRoom : Cell);
		var box = rooms.Count == 0
			? (Left: double.MaxValue, Top: double.MaxValue, Right: double.MinValue, Bottom: double.MinValue)
			: (Left: rooms.Min(r => r.X), Top: rooms.Min(r => r.Y), Right: Right(rooms), Bottom: Bottom(rooms));
		if (Space is not { } space || !includePlaces) return box;
		var places = IsPointy
			? (Left: (space.Left + Math.Min(space.Bottom, space.Top) / 2d) * HexHeight - HexHeight / 2, Top: -1.5 * HexSide * space.Top - HexSide,
				Right: (space.Right + Math.Max(space.Bottom, space.Top) / 2d) * HexHeight + HexHeight / 2, Bottom: -1.5 * HexSide * space.Bottom + HexSide)
			: IsHex
			? (Left: 1.5 * HexSide * space.Left - HexSide, Top: -(space.Top + Math.Max(space.Left, space.Right) / 2d) * HexHeight - HexHeight / 2,
				Right: 1.5 * HexSide * space.Right + HexSide, Bottom: -(space.Bottom + Math.Min(space.Left, space.Right) / 2d) * HexHeight + HexHeight / 2)
			: (Left: space.Left * Cell, Top: -(space.Top + 1d) * Cell, Right: (space.Right + 1d) * Cell, Bottom: -space.Bottom * Cell);
		return (Math.Min(box.Left, places.Left), Math.Min(box.Top, places.Top), Math.Max(box.Right, places.Right), Math.Max(box.Bottom, places.Bottom));
	}

	/// <summary>The cells a box of the drawing touches, from its drawing units.</summary>
	public static (int Left, int Bottom, int Right, int Top) CellsIn(double left, double top, double right, double bottom) =>
		((int)Math.Floor(left / Cell), (int)Math.Floor(-bottom / Cell), (int)Math.Floor(right / Cell), (int)Math.Floor(-top / Cell));

	/// <summary>
	/// The places a box of the drawing touches, as a box of x and y: its cells on a square map; on a hex map every place
	/// whose hexagon may reach into it, which reaches further north at its west end and further south at its east, or on
	/// a pointy hex map further west at its north end and further east at its south.
	/// </summary>
	public (int Left, int Bottom, int Right, int Top) PlacesIn(double left, double top, double right, double bottom)
	{
		if (!IsHex) return CellsIn(left, top, right, bottom);
		if (IsPointy)
		{
			var (south, north) = ((int)Math.Floor((-bottom - HexSide) / (1.5 * HexSide)), (int)Math.Ceiling((-top + HexSide) / (1.5 * HexSide)));
			return ((int)Math.Floor((left - HexHeight / 2) / HexHeight - north / 2d), south,
				(int)Math.Ceiling((right + HexHeight / 2) / HexHeight - south / 2d), north);
		}
		var (west, east) = ((int)Math.Floor((left - HexSide) / (1.5 * HexSide)), (int)Math.Ceiling((right + HexSide) / (1.5 * HexSide)));
		return (west, (int)Math.Floor(-bottom / HexHeight - east / 2d - 0.5), east, (int)Math.Ceiling(-top / HexHeight - west / 2d + 0.5));
	}

	/// <summary>The centre of the place at (x, y), in drawing units.</summary>
	public (double X, double Y) PlaceCentre(int x, int y) => IsPointy
		? ((x + y / 2d) * HexHeight, -1.5 * HexSide * y)
		: IsHex
		? (1.5 * HexSide * x, -(y + x / 2d) * HexHeight)
		: ((x + 0.5) * Cell, -(y + 0.5) * Cell);

	/// <summary>The box around the place at (x, y), in drawing units: its cell, or the box its hexagon fits in.</summary>
	public (double Left, double Top, double Width, double Height) PlaceBox(int x, int y)
	{
		if (!IsHex) return (x * Cell, -(y + 1) * Cell, Cell, Cell);
		var (cx, cy) = PlaceCentre(x, y);
		return (cx - HexWidth / 2, cy - HexTall / 2, HexWidth, HexTall);
	}

	/// <summary>
	/// The widest box that keeps clear of a hexagon's sides, around its centre, in drawing units: 1.2 sides wide and 0.8
	/// of its height when flat-topped, 0.8 of its width and 1.2 sides tall when pointy, so a room or a place's button
	/// drawn in it leaves the hexagon's edges and their walls showing.
	/// </summary>
	public (double Left, double Top, double Width, double Height) HexInside(int x, int y)
	{
		var (cx, cy) = PlaceCentre(x, y);
		var (width, height) = IsPointy ? (0.8 * HexHeight, 1.2 * HexSide) : (1.2 * HexSide, 0.8 * HexHeight);
		return (cx - width / 2, cy - height / 2, width, height);
	}

	/// <summary>The place under a point of the drawing.</summary>
	public (int X, int Y) PlaceAt(double x, double y)
	{
		if (!IsHex) return ((int)Math.Floor(x / Cell), (int)Math.Floor(-y / Cell));
		// Rounds axial to the nearest hexagon by its cube coordinates, whose three always sum to 0.
		var (q, r) = IsPointy
			? (x / HexHeight + y / (3 * HexSide), -y / (1.5 * HexSide))
			: (x / (1.5 * HexSide), -y / HexHeight - x / (3 * HexSide));
		var (rq, rr, rs) = (Math.Round(q), Math.Round(r), Math.Round(-q - r));
		var (dq, dr, ds) = (Math.Abs(rq - q), Math.Abs(rr - r), Math.Abs(rs + q + r));
		if (dq > dr && dq > ds) rq = -rr - rs;
		else if (dr > ds) rr = -rq - rs;
		return ((int)rq, (int)rr);
	}

	/// <summary>
	/// The corners of the hexagon at (x, y), shrunk toward its centre by <paramref name="scale"/>, clockwise on screen:
	/// when flat-topped from its east corner (east, south-east, south-west, west, north-west, north-east), and when
	/// pointy from its lower east one (lower east, south, lower west, upper west, north, upper east).
	/// </summary>
	public IReadOnlyList<(double X, double Y)> HexCorners(int x, int y, double scale = 1)
	{
		var (cx, cy) = PlaceCentre(x, y);
		var turn = IsPointy ? Math.PI / 6 : 0;
		return [.. Enumerable.Range(0, 6).Select(corner => (cx + scale * HexSide * Math.Cos(turn + corner * Math.PI / 3),
			cy + scale * HexSide * Math.Sin(turn + corner * Math.PI / 3)))];
	}

	/// <summary>
	/// The edge of the hexagon at (x, y) on <paramref name="side"/>, and the way out across it as a unit vector on screen;
	/// null for up, down or a side a hexagon does not have.
	/// </summary>
	public (double X1, double Y1, double X2, double Y2, double Dx, double Dy)? HexEdge(int x, int y, string side)
	{
		(int From, int To)? corners = (side, IsPointy) switch
		{
			("n", false) => (4, 5),
			("ne", false) => (5, 0),
			("se", false) => (0, 1),
			("s", false) => (1, 2),
			("sw", false) => (2, 3),
			("nw", false) => (3, 4),
			("e", true) => (5, 0),
			("se", true) => (0, 1),
			("sw", true) => (1, 2),
			("w", true) => (2, 3),
			("nw", true) => (3, 4),
			("ne", true) => (4, 5),
			_ => null,
		};
		if (corners is not var (from, to)) return null;
		var hex = HexCorners(x, y);
		var (cx, cy) = PlaceCentre(x, y);
		var (mx, my) = ((hex[from].X + hex[to].X) / 2, (hex[from].Y + hex[to].Y) / 2);
		var length = Math.Sqrt((mx - cx) * (mx - cx) + (my - cy) * (my - cy));
		return (hex[from].X, hex[from].Y, hex[to].X, hex[to].Y, (mx - cx) / length, (my - cy) / length);
	}

	/// <summary>
	/// The window of places to read for a view of <paramref name="view"/>'s cells: the view and half as much again on
	/// every side, within the stored places, and at most <see cref="MapCellsDto.MaxSide"/> cells a side around the view's
	/// middle. Null when the view holds no stored place.
	/// </summary>
	public (int Left, int Bottom, int Right, int Top)? Window((int Left, int Bottom, int Right, int Top) view)
	{
		if (Space is not { } space || view.Right < space.Left || view.Left > space.Right || view.Top < space.Bottom
				|| view.Bottom > space.Top) return null;
		var (left, right) = Span(view.Left, view.Right, space.Left, space.Right);
		var (bottom, top) = Span(view.Bottom, view.Top, space.Bottom, space.Top);
		return (left, bottom, right, top);

		static (int, int) Span(int from, int to, int min, int max)
		{
			var half = (to - from + 1) / 2;
			var (low, high) = (Math.Max(min, (long)from - half), Math.Min(max, (long)to + half));
			if (high - low + 1 <= MapCellsDto.MaxSide) return ((int)low, (int)high);
			var middle = Math.Clamp(((long)from + to) / 2, low, high);
			var start = Math.Clamp(middle - MapCellsDto.MaxSide / 2, low, high - MapCellsDto.MaxSide + 1);
			return ((int)start, (int)(start + MapCellsDto.MaxSide - 1));
		}
	}

	/// <summary>
	/// Whether <paramref name="cells"/> needs no new read for a view of <paramref name="view"/>'s cells on
	/// <paramref name="level"/>: it holds every stored place in view, or is already the most of them one read may hold.
	/// </summary>
	public bool Holds(MapCellsDto? cells, int level, (int Left, int Bottom, int Right, int Top) view)
	{
		if (Window(view) is not { } window || Space is not { } space) return true;
		if (cells is null || cells.Level != level) return false;
		var (left, bottom, right, top) = (Math.Max(view.Left, space.Left), Math.Max(view.Bottom, space.Bottom),
			Math.Min(view.Right, space.Right), Math.Min(view.Top, space.Top));
		return cells.Left <= left && cells.Right >= right && cells.Bottom <= bottom && cells.Top >= top
					 || (cells.Left, cells.Bottom, cells.Right, cells.Top) == window;
	}

	/// <summary>
	/// The point a room <paramref name="width"/> by <paramref name="height"/> pixels may be centred at nearest
	/// (<paramref name="x"/>, <paramref name="y"/>) with all of it on the picture, and whether that moved it; the point
	/// itself on a map with no picture. A room wider than the picture is centred on it.
	/// </summary>
	public (double X, double Y, bool Moved) InsidePicture(double x, double y, double width, double height)
	{
		if (Picture is not var (pictureWidth, pictureHeight)) return (x, y, false);
		var (halfW, halfH) = (Math.Min(width, pictureWidth) / 2, Math.Min(height, pictureHeight) / 2);
		var (fitX, fitY) = (Math.Round(Math.Clamp(x, halfW, pictureWidth - halfW)), Math.Round(Math.Clamp(y, halfH, pictureHeight - halfH)));
		return (fitX, fitY, fitX != Math.Round(x) || fitY != Math.Round(y));
	}

	/// <summary>How much of the picture <paramref name="room"/> covers: none for a room with no size, which is a point on it.</summary>
	public (double Width, double Height) Extent(MapPlacement room) => IsGraph && room.Room.Size is null ? (0, 0) : (room.Width, room.Height);

	/// <summary>The size a room may have on the picture: no wider or taller than it; the size itself with no picture.</summary>
	public (double Width, double Height) FitPicture(double width, double height) =>
		Picture is var (pictureWidth, pictureHeight) ? (Math.Min(width, pictureWidth), Math.Min(height, pictureHeight)) : (width, height);

	/// <summary>
	/// Where <paramref name="room"/> goes when taken <paramref name="dx"/> across and <paramref name="dy"/> down the drawing,
	/// in drawing units: to the nearest point on a picture, kept on it; to the nearest cell on a grid; to the hexagon its
	/// middle lands in on a hex map. Null when the cells it would cover hold another room of its level, which a grid or hex
	/// map draws one to a cell.
	/// </summary>
	public MapReshape? Moved(MapPlacement room, double dx, double dy)
	{
		if (IsGraph)
		{
			var (width, height) = Extent(room);
			var (x, y, moved) = InsidePicture(room.X + room.Width / 2 + dx, room.Y + room.Height / 2 + dy, width, height);
			return new(x, y, room.Level, AtEdge: moved);
		}
		if (IsHex)
		{
			var (q, r) = PlaceAt(room.X + room.Width / 2 + dx, room.Y + room.Height / 2 + dy);
			return Taken(room, q, r, 1, 1) ? null : new(q, r, room.Level);
		}
		var (columns, rows) = (Math.Max(1, (int)Math.Round(room.Width / Cell)), Math.Max(1, (int)Math.Round(room.Height / Cell)));
		var column = (int)Math.Round((room.X + dx) / Cell);
		var row = (int)Math.Round(-(room.Y + dy + room.Height) / Cell);
		return Taken(room, column, row, columns, rows) ? null : new(column, row, room.Level);
	}

	/// <summary>
	/// Where an arrow key takes <paramref name="room"/>: <paramref name="dx"/> and <paramref name="dy"/> are -1, 0 or 1
	/// across and down the screen. A grid room moves a cell, a picture's room <see cref="PictureStep"/> pixels (five times
	/// that with <paramref name="big"/>). A flat-topped hexagon has no east or west neighbour: left and right step to its
	/// lower west and east neighbours, or with <paramref name="big"/> its upper ones; a pointy one has no north or south,
	/// so up and down step north-east and south-west, or with <paramref name="big"/> north-west and south-east.
	/// </summary>
	public MapReshape? Nudged(MapPlacement room, int dx, int dy, bool big)
	{
		if (IsGraph)
		{
			var step = PictureStep * (big ? 5 : 1);
			return Moved(room, dx * step, dy * step);
		}
		var (x, y, _) = Spot(room);
		var (q, r) = ((int)x, (int)y);
		var (stepX, stepY) = (IsHex, IsPointy, dx, dy) switch
		{
			(true, false, 0, _) => (0, -dy),
			(true, false, 1, _) => big ? (1, 0) : (1, -1),
			(true, false, -1, _) => big ? (-1, 1) : (-1, 0),
			(true, true, _, 0) => (dx, 0),
			(true, true, _, -1) => big ? (-1, 1) : (0, 1),
			(true, true, _, 1) => big ? (1, -1) : (0, -1),
			_ => (dx, -dy),
		};
		if (IsHex) return Taken(room, q + stepX, r + stepY, 1, 1) ? null : new(q + stepX, r + stepY, room.Level);
		var (columns, rows) = (Math.Max(1, (int)Math.Round(room.Width / Cell)), Math.Max(1, (int)Math.Round(room.Height / Cell)));
		return Taken(room, q + stepX, r + stepY, columns, rows) ? null : new(q + stepX, r + stepY, room.Level);
	}

	/// <summary>Whether a room may be given a size on this map: a hex map's rooms are each one hexagon.</summary>
	public bool Resizable => !IsHex;

	/// <summary>
	/// <paramref name="room"/> with its edges moved, in drawing units: <paramref name="left"/> and <paramref name="top"/>
	/// move its north-west corner, <paramref name="right"/> and <paramref name="bottom"/> its south-east one, and the
	/// other edges stay. A grid room's edges go to the nearest cell lines, at least one cell apart; a picture's room's to
	/// whole pixels, at least <see cref="MinGraphRoom"/> apart and inside the picture. Null on a hex map, or where the grid
	/// cells it would cover hold another room.
	/// </summary>
	public MapReshape? Resized(MapPlacement room, double left, double top, double right, double bottom)
	{
		if (IsHex) return null;
		var (l, t, r, b) = (room.X + left, room.Y + top, room.X + room.Width + right, room.Y + room.Height + bottom);
		if (IsGraph)
		{
			var edge = false;
			if (Picture is var (pictureWidth, pictureHeight))
			{
				var (cl, ct, cr, cb) = (Math.Max(0, l), Math.Max(0, t), Math.Min(pictureWidth, r), Math.Min(pictureHeight, b));
				edge = (cl, ct, cr, cb) != (l, t, r, b);
				(l, t, r, b) = (cl, ct, cr, cb);
			}
			(l, t, r, b) = (Math.Round(l), Math.Round(t), Math.Round(r), Math.Round(b));
			if (r - l < MinGraphRoom) (l, r) = left != 0 ? (r - MinGraphRoom, r) : (l, l + MinGraphRoom);
			if (b - t < MinGraphRoom) (t, b) = top != 0 ? (b - MinGraphRoom, b) : (t, t + MinGraphRoom);
			return new((l + r) / 2, (t + b) / 2, room.Level, r - l, b - t, edge);
		}
		var (cellL, cellT, cellR, cellB) = ((int)Math.Round(l / Cell), (int)Math.Round(t / Cell), (int)Math.Round(r / Cell), (int)Math.Round(b / Cell));
		if (cellR - cellL < 1) (cellL, cellR) = left != 0 ? (cellR - 1, cellR) : (cellL, cellL + 1);
		if (cellB - cellT < 1) (cellT, cellB) = top != 0 ? (cellB - 1, cellB) : (cellT, cellT + 1);
		var (columns, rows) = (cellR - cellL, cellB - cellT);
		return Taken(room, cellL, -cellB, columns, rows) ? null : new(cellL, -cellB, room.Level, columns, rows);
	}

	/// <summary>
	/// Whether the cells <paramref name="columns"/> by <paramref name="rows"/> from the south-west cell (x, y), or on a hex
	/// map the hexagon at (x, y), hold a room of <paramref name="room"/>'s level other than it.
	/// </summary>
	private bool Taken(MapPlacement room, int x, int y, int columns, int rows) => On(room.Level).Where(other => !other.Region && other.Room.Id != room.Room.Id)
		.Any(other =>
		{
			var (ox, oy, _) = Spot(other);
			if (IsHex) return (int)ox == x && (int)oy == y;
			var (oc, or) = (Math.Max(1, (int)Math.Round(other.Width / Cell)), Math.Max(1, (int)Math.Round(other.Height / Cell)));
			return x < ox + oc && ox < x + columns && y < oy + or && oy < y + rows;
		});

	private static double Right(List<MapPlacement> rooms) => rooms.Count == 0 ? 0 : rooms.Max(r => r.X + r.Width);
	private static double Bottom(List<MapPlacement> rooms) => rooms.Count == 0 ? 0 : rooms.Max(r => r.Y + r.Height);

	private double Side(MapRoomDto room, int axis) => IsGraph
		? room.Size is { Length: > 1 } size ? Math.Max(size[axis], MinGraphRoom) : GraphRoom
		: (room.Size is { Length: > 1 } cells ? cells[axis] : 1) * Cell;

	private Dictionary<string, MapPlacement> Graph(IEnumerable<MapRoomDto> rooms) =>
		rooms.Where(room => room.Position is [_, _, ..]).DistinctBy(room => room.Id).ToDictionary(room => room.Id, room =>
		{
			var (width, height) = (Side(room, 0), Side(room, 1));
			// A graph room's position is its centre on the picture; a room reaching past the picture is drawn at its edge.
			var (left, top) = (room.Position![0] - width / 2, room.Position[1] - height / 2);
			if (Picture is not var (pictureWidth, pictureHeight)) return new MapPlacement(room, left, top, width, height, 0, Placed: true);
			// A room with no size is a point: its point stays on the picture, and the box drawn around it may reach over the edge.
			var (fitWidth, fitHeight) = room.Size is null ? (0, 0) : (Math.Min(width, pictureWidth), Math.Min(height, pictureHeight));
			var (x, y) = (Math.Clamp(room.Position[0], fitWidth / 2, pictureWidth - fitWidth / 2),
				Math.Clamp(room.Position[1], fitHeight / 2, pictureHeight - fitHeight / 2));
			var (drawWidth, drawHeight) = room.Size is null ? (width, height) : (fitWidth, fitHeight);
			return new MapPlacement(room, x - drawWidth / 2, y - drawHeight / 2, drawWidth, drawHeight, 0, Placed: true,
				Outside: (x, y, drawWidth, drawHeight) != (room.Position[0], room.Position[1], width, height));
		});

	private Dictionary<string, MapPlacement> Grid(IEnumerable<MapRoomDto> rooms) =>
		rooms.Where(room => room.Position is [_, _, ..]).DistinctBy(room => room.Id).ToDictionary(room => room.Id, room =>
			Cellular(room, room.Position![0], room.Position[1], room.Position.Length > 2 ? (int)Math.Round(room.Position[2]) : 0));

	/// <summary>
	/// A room whose south-west cell is (x, y): its top-left corner is the north-west of the area it covers. On a hex map
	/// a room stands inside the one hexagon it is on, clear of its sides.
	/// </summary>
	private MapPlacement Cellular(MapRoomDto room, double x, double y, int z)
	{
		if (IsHex)
		{
			// The room's box is drawn and named an inset in from its edges, so the inset goes around the box it fills.
			var (left, top, hexWidth, hexHeight) = HexInside((int)Math.Round(x), (int)Math.Round(y));
			return new(room, left - RoomInset, top - RoomInset, hexWidth + 2 * RoomInset, hexHeight + 2 * RoomInset, z, Placed: true);
		}
		var (width, height) = (Side(room, 0), Side(room, 1));
		return new(room, x * Cell, -(y + 1) * Cell - (height - Cell), width, height, z, Placed: true);
	}

	private Dictionary<string, MapPlacement> Compass(MapDto map)
	{
		var placed = Grid(map.Rooms);
		var cells = new Dictionary<(int X, int Y, int Z), string>();
		var cellOf = new Dictionary<string, (int X, int Y, int Z)>();
		// Two stored rooms in one cell: the lower dbref keeps it.
		foreach (var p in placed.Values.OrderBy(p => Number(p.Room.Id)).Where(p => cells.TryAdd(CellOf(p), p.Room.Id)))
		{
			cellOf[p.Room.Id] = CellOf(p);
		}
		var byId = map.Rooms.ToDictionary(room => room.Id);
		// Each compass exit is a step both ways: north from A to B puts B north of A, and A south of B.
		var steps = new Dictionary<string, List<(string To, (int X, int Y, int Z) Step)>>();
		var compass = map.Exits.OrderBy(exit => Number(exit.Id))
			.Select(exit => (From: exit.Source, To: exit.Destination ?? "", Step: Direction(exit, Space?.Kind)))
			.Where(exit => exit.Step is not null && byId.ContainsKey(exit.From) && byId.ContainsKey(exit.To));
		foreach (var (from, to, direction) in compass)
		{
			var step = direction.GetValueOrDefault();
			Steps(from).Add((to, step));
			Steps(to).Add((from, (-step.X, -step.Y, -step.Z)));
		}

		var walk = new Queue<string>(cellOf.Keys.OrderBy(Number));
		Walk();
		// A room no walk reached starts one of its own, east of everything drawn so far, so two wings never overlap.
		// The viewer's room goes first, so the part of the map they stand in keeps its shape.
		// Lazily filtered: each walk places more rooms, which the next test must see.
		var unreached = map.Rooms.OrderBy(room => room.Id == map.Here ? 0 : 1).ThenBy(room => Number(room.Id))
			.Where(room => !cellOf.ContainsKey(room.Id));
		foreach (var room in unreached)
		{
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
