using System.Buffers.Binary;
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

/// <summary>The sides of a place, and the step each takes.</summary>
public enum WorldSide
{
	North,
	East,
	South,
	West,
	Up,
	Down,
}

/// <summary>
/// How a stored map's places sit in its tiles. A tile is 32 by 32 places of one level; each place is four bytes,
/// little-endian: its terrain (0 for nothing, else an index into the map's palette plus one), then the edges it owns.
/// A place owns its north, east and up edges, three bits each (<see cref="WorldEdge"/>); its south, west and down
/// edges are the north, east and up edges of the places beside it.
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

	private static int Index(int column, int row) => (row * TileSide + column) * CellBytes;

	public static ushort Terrain(ReadOnlySpan<byte> tile, int column, int row) =>
		BinaryPrimitives.ReadUInt16LittleEndian(tile[Index(column, row)..]);

	public static void SetTerrain(Span<byte> tile, int column, int row, ushort terrain) =>
		BinaryPrimitives.WriteUInt16LittleEndian(tile[Index(column, row)..], terrain);

	public static ushort Edges(ReadOnlySpan<byte> tile, int column, int row) =>
		BinaryPrimitives.ReadUInt16LittleEndian(tile[(Index(column, row) + 2)..]);

	public static void SetEdges(Span<byte> tile, int column, int row, ushort edges) =>
		BinaryPrimitives.WriteUInt16LittleEndian(tile[(Index(column, row) + 2)..], edges);

	/// <summary>The bit position of an owned edge: north, east and up.</summary>
	private static int Shift(WorldSide owned) => owned switch
	{
		WorldSide.North => 0,
		WorldSide.East => 3,
		WorldSide.Up => 6,
		_ => throw new ArgumentOutOfRangeException(nameof(owned), owned, "A place owns only its north, east and up edges."),
	};

	public static WorldEdge Edge(ushort edges, WorldSide owned) => (WorldEdge)((edges >> Shift(owned)) & 0b111);

	public static ushort WithEdge(ushort edges, WorldSide owned, WorldEdge edge) =>
		(ushort)((edges & ~(0b111 << Shift(owned))) | ((int)edge << Shift(owned)));

	/// <summary>The step a side takes.</summary>
	public static (int X, int Y, int Z) Step(WorldSide side) => side switch
	{
		WorldSide.North => (0, 1, 0),
		WorldSide.East => (1, 0, 0),
		WorldSide.South => (0, -1, 0),
		WorldSide.West => (-1, 0, 0),
		WorldSide.Up => (0, 0, 1),
		_ => (0, 0, -1),
	};

	/// <summary>
	/// Where the edge on <paramref name="side"/> of (x, y, z) is stored: on that place for north, east and up, on the
	/// place beside it otherwise, with whether the edge is seen from the other side.
	/// </summary>
	public static (int X, int Y, int Z, WorldSide Owned, bool Flipped) Owner(int x, int y, int z, WorldSide side) => side switch
	{
		WorldSide.South => (x, y - 1, z, WorldSide.North, true),
		WorldSide.West => (x - 1, y, z, WorldSide.East, true),
		WorldSide.Down => (x, y, z - 1, WorldSide.Up, true),
		_ => (x, y, z, side, false),
	};

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
		_ => "d",
	};

	public static string Word(WorldSide side) => side.ToString().ToLowerInvariant();

	/// <summary>A side by its letter or its word, any case.</summary>
	public static WorldSide? ParseSide(string text) => text.Trim().ToLowerInvariant() switch
	{
		"n" or "north" => WorldSide.North,
		"e" or "east" => WorldSide.East,
		"s" or "south" => WorldSide.South,
		"w" or "west" => WorldSide.West,
		"u" or "up" => WorldSide.Up,
		"d" or "down" => WorldSide.Down,
		_ => null,
	};
}
