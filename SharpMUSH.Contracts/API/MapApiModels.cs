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
	/// For a viewer who <see cref="CanEdit"/>: the rooms this map's exits lead to that are on no map and that they
	/// control, which they may add to it. Empty for anyone else.
	/// </summary>
	public IReadOnlyList<MapRoomDto> Nearby { get; init; } = [];
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

	/// <summary>The connected players the viewer may know are in the room, by name: as <c>loc()</c> would find them, and not DARK or hidden.</summary>
	public IReadOnlyList<string> People { get; init; } = [];
}

/// <summary>How a map draws the rooms of one kind.</summary>
/// <param name="Type">The kind, as a room's <c>ENVIRONMENT</c> names it, in lower case.</param>
/// <param name="Label">What the legend calls it: its <c>LABEL</c>, or the kind itself when unset.</param>
/// <param name="Tone">A <c>tone()</c> colour's name (<c>info</c>, <c>green</c>); null when unset.</param>
/// <param name="Icon">An icon's name, or a character or two drawn as text; null when unset.</param>
public sealed record MapLegendDto(string Type, string Label, string? Tone, string? Icon);

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
}

/// <summary>Where the viewer stands, for a map list that opens on the map they are on.</summary>
/// <param name="Room">The viewer's room.</param>
/// <param name="Name">That room's name.</param>
/// <param name="Map">The map the room is on, when it is on one the viewer may see.</param>
public sealed record MapHereDto(string Room, string Name, MapSummaryDto? Map);

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
