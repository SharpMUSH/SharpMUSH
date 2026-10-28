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
}

/// <param name="Id">The room's dbref.</param>
/// <param name="Position">
/// Where the room is, as its map's kind reads it: x, y and z from <c>MAP`COORDS</c> on a coordinate or compass map, x and
/// y from <c>MAP`POS</c> on a graph map. A missing axis is 0; null when the room has none, or one is not a number.
/// </param>
/// <param name="Size">The room's <c>MAP`SIZE`W</c> and <c>MAP`SIZE`H</c>, each at least 1 when set; null when unset (1 by 1).</param>
public sealed record MapRoomDto(string Id, string Name, double[]? Position, double[]? Size);

/// <param name="Id">The exit's dbref.</param>
/// <param name="Aliases">The exit's other names, as <c>@name</c> takes them after <c>;</c>.</param>
/// <param name="Source">The room it leaves from.</param>
/// <param name="Destination">The room it leads to, when that is a room the viewer may see; null otherwise.</param>
/// <param name="DestinationMap">The map that room is on, when that is not this one and the viewer may see it.</param>
public sealed record MapExitDto(string Id, string Name, IReadOnlyList<string> Aliases, string Source, string? Destination,
	MapSummaryDto? DestinationMap);

/// <summary>The way from where the viewer stands to a room, over exits they may see, crossing maps where it must.</summary>
/// <param name="Steps">Each exit in turn; empty when the viewer is already there.</param>
/// <param name="Command">
/// <c>walk north up</c>: one line that takes the route, each exit by its name or, when that has a space, by an alias
/// without one. Null when an exit has no such name, or there are no steps.
/// </param>
public sealed record MapRouteDto(IReadOnlyList<MapStepDto> Steps, string? Command);

/// <param name="Exit">The exit's dbref.</param>
/// <param name="Name">The exit's name.</param>
/// <param name="Source">The room it leaves from.</param>
/// <param name="Destination">The room it leads to.</param>
/// <param name="DestinationName">That room's name.</param>
/// <param name="Map">The map that room is on, when that is a map the viewer may see; null otherwise.</param>
public sealed record MapStepDto(string Exit, string Name, string Source, string Destination, string DestinationName,
	MapSummaryDto? Map);
