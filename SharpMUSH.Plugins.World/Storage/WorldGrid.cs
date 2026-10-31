using System.Buffers.Binary;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Plugins.World.Storage;

/// <summary>What stands on one edge between two places.</summary>
public enum WorldEdge : byte
{
	/// <summary>Nothing: the way is open.</summary>
	Open = 0,

	/// <summary>Nothing passes either way.</summary>
	Wall = 1,

	/// <summary>Passable only out of the place the edge is stored on.</summary>
	OneWayOut = 2,

	/// <summary>Passable only into the place the edge is stored on.</summary>
	OneWayIn = 3,

	/// <summary>Passable for whoever passes its lock.</summary>
	Lock = 4,
}

/// <summary>
/// The sides of a place, and the step each takes. A square place has north, east, south and west; a flat-topped hex
/// place has north, northeast, southeast, south, southwest and northwest; a pointy-topped one has northeast, east,
/// southeast, southwest, west and northwest. All have up and down. A pointy place's northeast and southwest step
/// differently from a flat one's, so they are sides of their own.
/// </summary>
public enum WorldSide
{
	North,
	East,
	South,
	West,
	Up,
	Down,
	NorthEast,
	SouthEast,
	SouthWest,
	NorthWest,
	PointyNorthEast,
	PointySouthWest,
}

/// <summary>
/// How a stored map's places sit in its tiles. A tile is 32 by 32 places of one level; each place is four bytes,
/// little-endian: its terrain (0 for nothing, else an index into the map's palette plus one), then the edges it owns,
/// three bits each (<see cref="WorldEdge"/>). A square place owns its north, east and up edges; its south, west and
/// down edges are the north, east and up edges of the places beside it. A flat-topped hex place, at axial column x and
/// row y, owns its north, northeast, southeast and up edges in the same way, and a pointy-topped one its northeast,
/// east, southeast and up edges.
/// </summary>
public static class WorldGrid
{
	public const int TileSide = 32;
	public const int CellBytes = 4;
	public const int TileBytes = TileSide * TileSide * CellBytes;

	/// <summary>The furthest a place may be from 0 on any axis.</summary>
	public const int Reach = 1_000_000;

	/// <summary>The tile holding <paramref name="x"/>, and the column inside it.</summary>
	public static (int Tile, int Offset) Split(int x) => (Math.DivRem(x - Mod(x), TileSide).Quotient, Mod(x));

	private static int Mod(int value) => ((value % TileSide) + TileSide) % TileSide;

	public static bool InReach(int x, int y, int z) =>
		Math.Abs((long)x) <= Reach && Math.Abs((long)y) <= Reach && Math.Abs((long)z) <= Reach;

	/// <summary>A signed number as four bytes that sort as the number does.</summary>
	public static byte[] Ordered(int value)
	{
		var bytes = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(bytes, unchecked((uint)value ^ 0x8000_0000u));
		return bytes;
	}

	public static int ReadOrdered(ReadOnlySpan<byte> bytes) =>
		unchecked((int)(BinaryPrimitives.ReadUInt32BigEndian(bytes) ^ 0x8000_0000u));

	/// <summary>The key of a map's tile: the map, the level, the tile's column and row.</summary>
	public static byte[] TileKey(long map, int level, int tileX, int tileY) =>
		Keys.Concat(Keys.Dbref(map), Ordered(level), Ordered(tileX), Ordered(tileY));

	/// <summary>The key of a map's place record: the map, the level, the column and the row.</summary>
	public static byte[] PlaceKey(long map, int level, int x, int y) =>
		Keys.Concat(Keys.Dbref(map), Ordered(level), Ordered(x), Ordered(y));

	/// <summary>The prefix of one column of a map's place records on a level.</summary>
	public static byte[] PlaceColumn(long map, int level, int x) => Keys.Concat(Keys.Dbref(map), Ordered(level), Ordered(x));

	public static (int Level, int X, int Y) ReadPlaceKey(ReadOnlySpan<byte> key) =>
		(ReadOrdered(key[8..12]), ReadOrdered(key[12..16]), ReadOrdered(key[16..20]));

	/// <summary>The level and the tile's column and row, from a <see cref="TileKey"/>, laid out as a place key is.</summary>
	public static (int Level, int TileX, int TileY) ReadTileKey(ReadOnlySpan<byte> key) => ReadPlaceKey(key);

	private static int Index(int column, int row) => (row * TileSide + column) * CellBytes;

	public static ushort Terrain(ReadOnlySpan<byte> tile, int column, int row) =>
		BinaryPrimitives.ReadUInt16LittleEndian(tile[Index(column, row)..]);

	public static void SetTerrain(Span<byte> tile, int column, int row, ushort terrain) =>
		BinaryPrimitives.WriteUInt16LittleEndian(tile[Index(column, row)..], terrain);

	public static ushort Edges(ReadOnlySpan<byte> tile, int column, int row) =>
		BinaryPrimitives.ReadUInt16LittleEndian(tile[(Index(column, row) + 2)..]);

	public static void SetEdges(Span<byte> tile, int column, int row, ushort edges) =>
		BinaryPrimitives.WriteUInt16LittleEndian(tile[(Index(column, row) + 2)..], edges);

	/// <summary>The sides of a square place.</summary>
	public static readonly IReadOnlyList<WorldSide> SquareSides =
		[WorldSide.North, WorldSide.East, WorldSide.South, WorldSide.West, WorldSide.Up, WorldSide.Down];

	/// <summary>The sides of a hex place, clockwise from north, then up and down.</summary>
	public static readonly IReadOnlyList<WorldSide> HexSides =
	[
		WorldSide.North, WorldSide.NorthEast, WorldSide.SouthEast, WorldSide.South, WorldSide.SouthWest, WorldSide.NorthWest,
		WorldSide.Up, WorldSide.Down,
	];

	/// <summary>The sides of a pointy-topped hex place, clockwise from northeast, then up and down.</summary>
	public static readonly IReadOnlyList<WorldSide> PointyHexSides =
	[
		WorldSide.PointyNorthEast, WorldSide.East, WorldSide.SouthEast, WorldSide.PointySouthWest, WorldSide.West, WorldSide.NorthWest,
		WorldSide.Up, WorldSide.Down,
	];

	/// <summary>The sides a place in <paramref name="space"/> has; none on a graph map, whose places have ways instead.</summary>
	public static IReadOnlyList<WorldSide> Sides(string space) => space switch
	{
		MapSpaces.Graph => [],
		MapSpaces.Hex => HexSides,
		MapSpaces.PointyHex => PointyHexSides,
		_ => SquareSides,
	};

	/// <summary>The sides a place in <paramref name="space"/> owns: the edges stored on it.</summary>
	public static IReadOnlyList<WorldSide> Owned(string space) => space switch
	{
		MapSpaces.Graph => [],
		MapSpaces.Hex => [WorldSide.North, WorldSide.NorthEast, WorldSide.SouthEast, WorldSide.Up],
		MapSpaces.PointyHex => [WorldSide.PointyNorthEast, WorldSide.East, WorldSide.SouthEast, WorldSide.Up],
		_ => [WorldSide.North, WorldSide.East, WorldSide.Up],
	};

	/// <summary>A place's sides in <paramref name="space"/> by letter, as a refusal lists them: "n, e, s, w, u or d".</summary>
	public static string SideLetters(string space) =>
		$"{string.Join(", ", Sides(space).SkipLast(1).Select(Letter))} or {Letter(Sides(space)[^1])}";

	/// <summary>
	/// The bit position of an owned edge: north, east and up on a square place, north, northeast, southeast and up on a
	/// flat hex one, and on a pointy one east, southeast, up and its northeast in north's place, which it has no use for.
	/// </summary>
	private static int Shift(WorldSide owned) => owned switch
	{
		WorldSide.North or WorldSide.PointyNorthEast => 0,
		WorldSide.East => 3,
		WorldSide.Up => 6,
		WorldSide.NorthEast => 9,
		WorldSide.SouthEast => 12,
		_ => throw new ArgumentOutOfRangeException(nameof(owned), owned, "A place owns only its north, east, northeast, southeast and up edges."),
	};

	public static WorldEdge Edge(ushort edges, WorldSide owned) => (WorldEdge)((edges >> Shift(owned)) & 0b111);

	public static ushort WithEdge(ushort edges, WorldSide owned, WorldEdge edge) =>
		(ushort)((edges & ~(0b111 << Shift(owned))) | ((int)edge << Shift(owned)));

	/// <summary>
	/// The step a side takes. The hex sides step in axial coordinates, north being y + 1: a flat place's northeast is
	/// x + 1, a pointy place's y + 1.
	/// </summary>
	public static (int X, int Y, int Z) Step(WorldSide side) => side switch
	{
		WorldSide.North or WorldSide.PointyNorthEast => (0, 1, 0),
		WorldSide.East or WorldSide.NorthEast => (1, 0, 0),
		WorldSide.South or WorldSide.PointySouthWest => (0, -1, 0),
		WorldSide.West or WorldSide.SouthWest => (-1, 0, 0),
		WorldSide.SouthEast => (1, -1, 0),
		WorldSide.NorthWest => (-1, 1, 0),
		WorldSide.Up => (0, 0, 1),
		_ => (0, 0, -1),
	};

	/// <summary>The side across from <paramref name="side"/>.</summary>
	public static WorldSide Opposite(WorldSide side) => side switch
	{
		WorldSide.North => WorldSide.South,
		WorldSide.East => WorldSide.West,
		WorldSide.South => WorldSide.North,
		WorldSide.West => WorldSide.East,
		WorldSide.Up => WorldSide.Down,
		WorldSide.Down => WorldSide.Up,
		WorldSide.NorthEast => WorldSide.SouthWest,
		WorldSide.SouthEast => WorldSide.NorthWest,
		WorldSide.SouthWest => WorldSide.NorthEast,
		WorldSide.PointyNorthEast => WorldSide.PointySouthWest,
		WorldSide.PointySouthWest => WorldSide.PointyNorthEast,
		_ => WorldSide.SouthEast,
	};

	/// <summary>
	/// Where the edge on <paramref name="side"/> of (x, y, z) is stored: on that place for a side it owns, on the place
	/// beside it otherwise, with whether the edge is seen from the other side.
	/// </summary>
	public static (int X, int Y, int Z, WorldSide Owned, bool Flipped) Owner(int x, int y, int z, WorldSide side)
	{
		if (side is not (WorldSide.South or WorldSide.West or WorldSide.Down or WorldSide.SouthWest or WorldSide.NorthWest
			or WorldSide.PointySouthWest))
			return (x, y, z, side, false);
		var (dx, dy, dz) = Step(side);
		return (x + dx, y + dy, z + dz, Opposite(side), true);
	}

	/// <summary>An edge as the place on its other side sees it: a one-way edge points the other way.</summary>
	public static WorldEdge Flip(WorldEdge edge) => edge switch
	{
		WorldEdge.OneWayOut => WorldEdge.OneWayIn,
		WorldEdge.OneWayIn => WorldEdge.OneWayOut,
		_ => edge,
	};

	public static string Letter(WorldSide side) => side switch
	{
		WorldSide.North => "n",
		WorldSide.East => "e",
		WorldSide.South => "s",
		WorldSide.West => "w",
		WorldSide.Up => "u",
		WorldSide.Down => "d",
		WorldSide.NorthEast or WorldSide.PointyNorthEast => "ne",
		WorldSide.SouthEast => "se",
		WorldSide.SouthWest or WorldSide.PointySouthWest => "sw",
		_ => "nw",
	};

	public static string Word(WorldSide side) => side switch
	{
		WorldSide.PointyNorthEast => "northeast",
		WorldSide.PointySouthWest => "southwest",
		_ => side.ToString().ToLowerInvariant(),
	};

	/// <summary>A side by its letters or its word, any case, whatever space it belongs to.</summary>
	public static WorldSide? ParseSide(string text) => text.Trim().ToLowerInvariant() switch
	{
		"n" or "north" => WorldSide.North,
		"e" or "east" => WorldSide.East,
		"s" or "south" => WorldSide.South,
		"w" or "west" => WorldSide.West,
		"u" or "up" => WorldSide.Up,
		"d" or "down" => WorldSide.Down,
		"ne" or "northeast" => WorldSide.NorthEast,
		"se" or "southeast" => WorldSide.SouthEast,
		"sw" or "southwest" => WorldSide.SouthWest,
		"nw" or "northwest" => WorldSide.NorthWest,
		_ => null,
	};

	/// <summary>A side of a place in <paramref name="space"/>; null for no side, or one that space's places do not have.</summary>
	public static WorldSide? ParseSide(string text, string space) => (ParseSide(text), space) switch
	{
		(WorldSide.NorthEast, MapSpaces.PointyHex) => WorldSide.PointyNorthEast,
		(WorldSide.SouthWest, MapSpaces.PointyHex) => WorldSide.PointySouthWest,
		({ } side, _) when Sides(space).Contains(side) => side,
		_ => null,
	};
}
