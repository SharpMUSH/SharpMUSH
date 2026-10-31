namespace SharpMUSH.Library.API;

/// <summary>How a map is drawn: the values of a map object's <c>MAP`KIND</c>.</summary>
public static class MapKinds
{
	/// <summary>A grid: each room at its x, y and z, one level at a time, north up.</summary>
	public const string Coordinates = "coordinates";

	/// <summary>Placed by its exits: north is up, east is right, up and down change the level.</summary>
	public const string Compass = "compass";

	/// <summary>Rooms at their x and y anywhere, with no grid.</summary>
	public const string Graph = "graph";

	public static IReadOnlyList<string> All { get; } = [Coordinates, Compass, Graph];

	/// <summary>The kind <paramref name="value"/> names, ignoring case and spaces; null when it names none.</summary>
	public static string? Parse(string? value) =>
		All.FirstOrDefault(kind => string.Equals(kind, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A map as the map list shows it.</summary>
/// <param name="Id">The map object's dbref, <c>#12</c>.</param>
/// <param name="Kind">One of <see cref="MapKinds"/>.</param>
/// <param name="Parent">The dbref of the map it is zoned to, when that is a map the viewer may see: the region it is inside.</param>
public sealed record MapSummaryDto(string Id, string Name, string Kind, string? Parent);

/// <summary>One map: the rooms zoned to it that the viewer may see, and their exits.</summary>
/// <param name="Size">The map's <c>MAP`SIZE`W</c> and <c>MAP`SIZE`H</c>: cells on a coordinate map, the backdrop's pixels on a graph map; null when unset.</param>
/// <param name="Here">The viewer's room, when it is on this map.</param>
/// <param name="Truncated">The map has more rooms or exits than one read returns; some are left out.</param>
public sealed record MapDto(MapSummaryDto Map, double[]? Size, IReadOnlyList<MapRoomDto> Rooms, IReadOnlyList<MapExitDto> Exits,
	string? Here, bool Truncated)
{
	/// <summary>
	/// The maps zoned to this one that the viewer may see: the regions inside it, each placed as a room is, from the
	/// region's own <c>MAP`COORDS</c> or <c>MAP`POS</c> and <c>MAP`SIZE</c>.
	/// </summary>
	public IReadOnlyList<MapRoomDto> Regions { get; init; } = [];

	/// <summary>The region the viewer's room is inside, however deep, when it is inside one of <see cref="Regions"/>.</summary>
	public string? HereRegion { get; init; }

	/// <summary>The level the map opens on for a viewer who is not on it: its <c>MAP`LEVEL</c>; null when unset.</summary>
	public int? Level { get; init; }

	/// <summary>Level names by level: <c>MAP`LEVEL`-1</c> set to <c>Sewers</c> names level -1.</summary>
	public IReadOnlyDictionary<int, string> LevelNames { get; init; } = new Dictionary<int, string>();

	/// <summary>
	/// The picture a graph map is drawn on: its <c>MAP`BACKDROP</c>, an image URL, as stored. The portal shows only a site
	/// path or an https URL; null when unset.
	/// </summary>
	public string? Backdrop { get; init; }

	/// <summary>
	/// The kinds of place this map's rooms may be, from <c>MAP`LEGEND`&lt;type&gt;`TONE</c>, <c>`ICON</c> and <c>`LABEL</c> on
	/// the map and on the maps it is inside; the nearest map that names a type decides it.
	/// </summary>
	public IReadOnlyList<MapLegendDto> Legend { get; init; } = [];

	/// <summary>The viewer controls the map, so may plan changes to it: its builder mode.</summary>
	public bool CanEdit { get; init; }

	/// <summary>
	/// When a map source (a plugin) stores this map's places: the space they are in and how far they reach. The places
	/// are read a window at a time (<c>GET api/maps/{dbref}/cells</c>); <see cref="Rooms"/> still lists the rooms
	/// standing on the map. Null for a map of rooms alone.
	/// </summary>
	public MapSpaceDto? Space { get; init; }

	/// <summary>
	/// For a viewer who <see cref="CanEdit"/>: the rooms this map's exits lead to that are on no map and that they
	/// control, which they may add to it. Empty for anyone else.
	/// </summary>
	public IReadOnlyList<MapRoomDto> Nearby { get; init; } = [];

	/// <summary>
	/// For a viewer who <see cref="CanEdit"/>: how many more objects their quota lets them build, each room and each exit
	/// one; null when no quota limits them.
	/// </summary>
	public int? BuildsLeft { get; init; }
}

/// <param name="Id">The room's dbref.</param>
/// <param name="Position">
/// Where the room is, as its map's kind reads it: x, y and z from <c>MAP`COORDS</c> on a coordinate or compass map, x and
/// y from <c>MAP`POS</c> on a graph map. A missing axis is 0; null when the room has none, or one is not a number.
/// </param>
/// <param name="Size">The room's <c>MAP`SIZE`W</c> and <c>MAP`SIZE`H</c>, each at least 1 when set; null when unset (1 by 1).</param>
public sealed record MapRoomDto(string Id, string Name, double[]? Position, double[]? Size)
{
	/// <summary>The viewer controls both the map and this room, so may move it, size it or take it off the map.</summary>
	public bool Editable { get; init; }

	/// <summary>
	/// The kind of place the room is: its <c>ENVIRONMENT</c>, the attribute the gmcp-room package sends a MUD client's
	/// mapper, in lower case; null when unset. The map's <see cref="MapDto.Legend"/> says how a kind is drawn.
	/// </summary>
	public string? Environment { get; init; }

	/// <summary>The room stands on a place of the map's <see cref="MapDto.Space"/>: the map's own ways lead to it.</summary>
	public bool OnPlace { get; init; }

	/// <summary>The connected players the viewer may know are in the room, by name: as <c>loc()</c> would find them, and not DARK or hidden.</summary>
	public IReadOnlyList<string> People { get; init; } = [];
}

/// <summary>How a map draws the rooms of one kind.</summary>
/// <param name="Type">The kind, as a room's <c>ENVIRONMENT</c> names it, in lower case.</param>
/// <param name="Label">What the legend calls it: its <c>LABEL</c>, or the kind itself when unset.</param>
/// <param name="Tone">A <c>tone()</c> colour's name (<c>info</c>, <c>green</c>); null when unset.</param>
/// <param name="Icon">An icon's name, or a character or two drawn as text; null when unset.</param>
public sealed record MapLegendDto(string Type, string Label, string? Tone, string? Icon)
{
	/// <summary>Whether the entry comes from a map this one is zoned to, rather than the map itself.</summary>
	public bool Inherited { get; init; }

	/// <summary>
	/// For an entry the map holds itself, the entry a map it is zoned to gives the type, which shows again once the map's
	/// own is cleared; null when no such map names the type.
	/// </summary>
	public MapLegendDto? Parent { get; init; }
}

/// <param name="Id">The exit's dbref.</param>
/// <param name="Aliases">The exit's other names, as <c>@name</c> takes them after <c>;</c>.</param>
/// <param name="Source">The room it leaves from.</param>
/// <param name="Destination">The room it leads to, when that is a room the viewer may see; null otherwise.</param>
/// <param name="DestinationMap">The map that room is on, when that is not this one and the viewer may see it.</param>
public sealed record MapExitDto(string Id, string Name, IReadOnlyList<string> Aliases, string Source, string? Destination,
	MapSummaryDto? DestinationMap)
{
	/// <summary>The name of the room it leads to, whenever <see cref="Destination"/> is set, whether or not that room is on a map.</summary>
	public string? DestinationName { get; init; }

	/// <summary>The viewer controls both the map and this exit, so may rename it or destroy it.</summary>
	public bool Editable { get; init; }
}

/// <summary>Where the viewer stands, for a map list that opens on the map they are on.</summary>
/// <param name="Room">The viewer's room.</param>
/// <param name="Name">That room's name.</param>
/// <param name="Map">The map the room is on, when it is on one the viewer may see.</param>
public sealed record MapHereDto(string Room, string Name, MapSummaryDto? Map)
{
	/// <summary>The viewer may run <c>@create</c>, which is how a new map is made.</summary>
	public bool CanBuild { get; init; }
}

/// <summary>The way from where the viewer stands to a room, over exits they may see, crossing maps where it must.</summary>
/// <param name="Steps">Each exit in turn; empty when the viewer is already there.</param>
/// <param name="Command">
/// <c>walk north up</c>: one line that takes the route, each exit by its name or, when that has a space, by an alias
/// without one, or else by its name in double quotes. Null when an exit has no such name, or there are no steps.
/// </param>
public sealed record MapRouteDto(IReadOnlyList<MapStepDto> Steps, string? Command);

/// <summary>Which attribute a look shows as a room's description to the viewer: a reality layer's own, or <c>DESCRIBE</c>.</summary>
/// <param name="Attribute">The attribute's name.</param>
public sealed record MapDescriptionDto(string Attribute);

/// <param name="Exit">The exit's dbref.</param>
/// <param name="Name">The exit's name.</param>
/// <param name="Source">The room it leaves from.</param>
/// <param name="Destination">The room it leads to.</param>
/// <param name="DestinationName">That room's name.</param>
/// <param name="Map">The map that room is on, when that is a map the viewer may see; null otherwise.</param>
public sealed record MapStepDto(string Exit, string Name, string Source, string Destination, string DestinationName,
	MapSummaryDto? Map);

/// <summary>The spaces a map source may store a map's places in.</summary>
public static class MapSpaces
{
	/// <summary>Square cells at x and y, a level at a time; north is y + 1.</summary>
	public const string Square = "square";

	/// <summary>Flat-topped hexagons at axial q and r, a level at a time; their sides face n, ne, se, s, sw and nw.</summary>
	public const string Hex = "hex";

	/// <summary>Pointy-topped hexagons at axial q and r, a level at a time; their sides face ne, e, se, sw, w and nw.</summary>
	public const string PointyHex = "pointy-hex";

	/// <summary>Named places joined by named ways, with no grid.</summary>
	public const string Graph = "graph";

	public static IReadOnlyList<string> All { get; } = [Square, Hex, PointyHex, Graph];

	/// <summary>Whether <paramref name="kind"/> is either hex space.</summary>
	public static bool IsHex(string? kind) => kind is Hex or PointyHex;
}

/// <summary>
/// The space a map source stores a map's places in, and the box from <see cref="Left"/>, <see cref="Bottom"/> to
/// <see cref="Right"/>, <see cref="Top"/> (inclusive) that holds every place on every level.
/// </summary>
/// <param name="Kind">One of <see cref="MapSpaces"/>.</param>
/// <param name="Levels">The levels that hold a place, lowest first.</param>
public sealed record MapSpaceDto(string Kind, int Left, int Bottom, int Right, int Top, IReadOnlyList<int> Levels);

/// <summary>
/// The places of one level of a stored map inside a window, from <see cref="Left"/>, <see cref="Bottom"/> to
/// <see cref="Right"/>, <see cref="Top"/> inclusive, as the viewer may see them. A cell that holds nothing is left out.
/// </summary>
/// <param name="Walls">The walls, one-way edges and locked ways on the edges of these cells, each once.</param>
/// <param name="Truncated">The window holds more places than one read returns; some are left out.</param>
public sealed record MapCellsDto(int Level, int Left, int Bottom, int Right, int Top, IReadOnlyList<MapCellDto> Cells,
	IReadOnlyList<MapWallDto> Walls, bool Truncated)
{
	/// <summary>The most cells one window may cover: 128 by 128.</summary>
	public const int MaxCells = MaxSide * MaxSide;

	/// <summary>The widest and tallest a square window may be, in cells.</summary>
	public const int MaxSide = 128;
}

/// <summary>One place of a stored map, drawn from its terrain type, its own data and the map's render format.</summary>
/// <param name="Type">Its terrain type, a legend type of the map (<see cref="MapDto.Legend"/>).</param>
public sealed record MapCellDto(int X, int Y, string Type)
{
	/// <summary>The place's id: the map's objid and its position, <c>#120:1761800000/3,1,0</c>.</summary>
	public string? Id { get; init; }

	/// <summary>The place's own name; null when it is named only by its terrain.</summary>
	public string? Name { get; init; }

	/// <summary>The room standing on the place, when one does and the viewer may see it.</summary>
	public string? Room { get; init; }

	/// <summary>The room standing on it is a permanent room rather than one lent while someone is there.</summary>
	public bool Permanent { get; init; }

	/// <summary>The place cannot be entered: its terrain is closed.</summary>
	public bool Closed { get; init; }

	/// <summary>Why it cannot be entered, as a player who tries is told; null when it can.</summary>
	public string? Reason { get; init; }

	/// <summary>An icon name, or a character or two, overriding the legend's; null to use the legend's.</summary>
	public string? Icon { get; init; }

	/// <summary>A <c>tone()</c> colour's name overriding the legend's; null to use the legend's.</summary>
	public string? Tone { get; init; }

	/// <summary>What the place is called on the map overriding the legend's label; null to use the legend's.</summary>
	public string? Label { get; init; }

	/// <summary>A short marker drawn on the cell; null for none.</summary>
	public string? Badge { get; init; }

	/// <summary>A picture for the place's panel: a site path or an https URL; null for none.</summary>
	public string? Image { get; init; }

	/// <summary>Labelled values the place's panel shows, in order.</summary>
	public IReadOnlyList<MapInfoDto> Info { get; init; } = [];

	/// <summary>How many connected players the viewer may know are on the place.</summary>
	public int People { get; init; }
}

/// <summary>One labelled value in a place's panel.</summary>
public sealed record MapInfoDto(string Label, string Value);

/// <summary>Kinds of <see cref="MapWallDto"/>.</summary>
public static class MapWallKinds
{
	/// <summary>Nothing passes either way.</summary>
	public const string Wall = "wall";

	/// <summary>Passable only out of the cell it is on, through <see cref="MapWallDto.Side"/>.</summary>
	public const string OneWay = "oneway";

	/// <summary>Passable for whoever passes its lock; the map does not try it.</summary>
	public const string Lock = "lock";
}

/// <summary>An edge of the cell at <see cref="X"/>, <see cref="Y"/> that is not simply open.</summary>
/// <param name="Side">The side: <c>n</c>, <c>e</c>, <c>s</c>, <c>w</c>, <c>u</c> or <c>d</c>.</param>
/// <param name="Kind">One of <see cref="MapWallKinds"/>.</param>
/// <param name="Reason">What a player who is refused is told; null when the map does not say.</param>
public sealed record MapWallDto(int X, int Y, string Side, string Kind, string? Reason);
