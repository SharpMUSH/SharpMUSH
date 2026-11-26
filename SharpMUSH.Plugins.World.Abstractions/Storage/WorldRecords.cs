namespace SharpMUSH.Plugins.World.Storage;

/// <summary>What the store keeps about one stored map.</summary>
/// <param name="Space">The space its places are in: <c>square</c>, <c>hex</c>, <c>pointy-hex</c> or <c>graph</c>.</param>
/// <param name="Created">The map object's creation time, so a record never passes to an object that reuses its number.</param>
/// <param name="Palette">The terrain types painted on it, by terrain id less one.</param>
public sealed record WorldMapRecord(string Space, long Created, List<string> Palette)
{
	/// <summary>Whether anything has been painted, and so whether the box below means anything.</summary>
	public bool Painted { get; init; }

	/// <summary>The lowest column painted.</summary>
	public int Left { get; init; }

	/// <summary>The lowest row painted.</summary>
	public int Bottom { get; init; }

	/// <summary>The highest column painted.</summary>
	public int Right { get; init; }

	/// <summary>The highest row painted.</summary>
	public int Top { get; init; }

	/// <summary>The levels anything has been painted on, lowest first.</summary>
	public List<int> Levels { get; init; } = [];

	/// <summary>The terrain type a terrain id stands for; null for 0 or an id the palette does not have.</summary>
	public string? Type(ushort terrain) => terrain > 0 && terrain <= Palette.Count ? Palette[terrain - 1] : null;
}

/// <summary>What a place has beyond its terrain and edges.</summary>
/// <param name="Name">Its own name.</param>
/// <param name="Data">A JSON object of whatever the game keeps there.</param>
/// <param name="Reasons">Why each edge it owns refuses a mover, by side letter (<c>n</c>, <c>e</c>, <c>ne</c>, <c>se</c>, <c>u</c>).</param>
public sealed record WorldPlaceRecord(string? Name, string? Data, Dictionary<string, string>? Reasons)
{
	/// <summary>A place with nothing kept about it.</summary>
	public static readonly WorldPlaceRecord Empty = new(null, null, null);

	/// <summary>The lock key of each locked edge it owns, by side letter, bound to objids as <c>@lock</c> stores one.</summary>
	public Dictionary<string, string>? Keys { get; init; }

	/// <summary>Whether nothing is kept about the place.</summary>
	public bool IsEmpty => Name is null && Data is null && Reasons is not { Count: > 0 } && Keys is not { Count: > 0 };
}

/// <summary>One place read out of the store: its terrain id and the edges it owns.</summary>
public readonly record struct WorldCell(ushort Terrain, ushort Edges)
{
	/// <summary>Whether nothing is painted there.</summary>
	public bool IsEmpty => Terrain == 0 && Edges == 0;
}

/// <summary>A room standing on a place of a stored map.</summary>
/// <param name="Map">The map's number.</param>
/// <param name="MapCreated">The map's creation time, so the record never passes to an object that reuses its number.</param>
/// <param name="X">The place's column.</param>
/// <param name="Y">The place's row.</param>
/// <param name="Z">The place's level.</param>
/// <param name="Created">The room's creation time, for the same reason.</param>
/// <param name="Permanent">Whether it was placed there to stay; otherwise it was lent and goes back once empty.</param>
public sealed record WorldRoomRecord(long Map, long MapCreated, int X, int Y, int Z, long Created, bool Permanent)
{
	/// <summary>Since when a lent room has held nothing, in Unix milliseconds; null while it holds something.</summary>
	public long? EmptySince { get; init; }
}
