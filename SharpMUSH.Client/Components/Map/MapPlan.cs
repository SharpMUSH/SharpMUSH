using System.Globalization;
using System.Text.RegularExpressions;
using SharpMUSH.Library.API;

namespace SharpMUSH.Client.Components.Map;

/// <summary>
/// One command of a planned change, and how to tell that it worked: <see cref="Check"/> is read right after it, in the
/// same queue entry, and must give <see cref="Expect"/>, or a dbref when that is null. <c>{new:1}</c> in either stands
/// for the room the plan digs as <c>new:1</c>, once it exists.
/// </summary>
public sealed record MapStep(string Command, string Check, string? Expect);

/// <summary>A change a builder has planned: what it does, to which room, and the commands that make it, in order.</summary>
/// <param name="Room">The room it changes, or the key of the room it digs (<c>new:1</c>).</param>
/// <param name="Name">That room's name.</param>
public sealed record MapChange(string Key, MapChangeKind Kind, string Room, string Name, IReadOnlyList<MapStep> Steps)
{
	/// <summary>What the review names besides: a new name, the room a way leads to, a level's number.</summary>
	public string? Other { get; init; }
}

public enum MapChangeKind
{
	Place,
	Size,
	Add,
	Remove,
	Dig,
	Describe,
	Rename,
	Join,
	RenameExit,
	RemoveExit,
	NameLevel,
	RenameMap,
	MoveMap,
	MapSetting,
	Legend,
	CreateWorld,
	OpenWays,
	Paint,
	Side,
	NamePlace,
	HidePlace,
	PlaceRoom,
	Link,
	Unlink,
}

/// <summary>
/// A builder's changes to one map, kept in the portal until they are applied: nothing is written before then. The plan
/// writes each change as the commands a builder would type, and shows the map as it will be once they have run. No UI
/// state, so the page and the tests share it.
/// </summary>
public sealed partial class MapPlan(MapDto map)
{
	private readonly List<MapChange> _changes = [];
	private readonly Dictionary<string, MapRoomDto> _dug = new();
	private readonly Dictionary<string, (string? From, string To, string? Back)> _dugExits = new();
	private readonly Dictionary<string, MapRoomDto> _added = new();
	private readonly Dictionary<string, string> _dugDescriptions = new();
	private readonly Dictionary<string, (string From, string To, string Names, string? Back)> _joins = new();
	private readonly Dictionary<string, string> _elsewhere = new();
	private int _next;

	public MapDto Map { get; } = map;

	public IReadOnlyList<MapChange> Changes => _changes;

	private bool IsGraph => Map.Map.Kind == MapKinds.Graph;

	/// <summary>Puts <paramref name="room"/> at a cell (x, y on level z) or, on a graph map, at a point of its picture.</summary>
	public void Place(string room, double x, double y, int z = 0)
	{
		if (_dug.TryGetValue(room, out var dug))
		{
			Redig(room, dug with { Position = IsGraph ? [x, y] : [x, y, z] });
			return;
		}
		var target = Room(room);
		var steps = new List<MapStep>();
		if (IsGraph)
		{
			steps.Add(Set(room, "MAP`POS`X", Num(x)));
			steps.Add(Set(room, "MAP`POS`Y", Num(y)));
		}
		else
		{
			steps.Add(Set(room, "MAP`COORDS`X", Num(x)));
			steps.Add(Set(room, "MAP`COORDS`Y", Num(y)));
			if (z != 0 || target?.Position is [_, _, not 0, ..]) steps.Add(Set(room, "MAP`COORDS`Z", Num(z)));
		}
		Upsert(new($"place:{room}", MapChangeKind.Place, room, Name(room), steps));
	}

	/// <summary>Makes <paramref name="room"/> cover <paramref name="width"/> by <paramref name="height"/> cells (pixels on a graph map); 1 by 1 clears it.</summary>
	public void Size(string room, double width, double height)
	{
		if (_dug.TryGetValue(room, out var dug))
		{
			Redig(room, dug with { Size = width <= 1 && height <= 1 ? null : [width, height] });
			return;
		}
		var clear = width <= 1 && height <= 1;
		Upsert(new($"size:{room}", MapChangeKind.Size, room, Name(room),
		[
			Set(room, "MAP`SIZE`W", clear ? "" : Num(width)),
			Set(room, "MAP`SIZE`H", clear ? "" : Num(height)),
		]));
	}

	/// <summary>
	/// Sets <paramref name="room"/>'s description: each line break written as <c>%r</c>, as a client would type it.
	/// Blank clears it.
	/// </summary>
	public void Describe(string room, string text)
	{
		var value = Description(text);
		if (_dug.ContainsKey(room))
		{
			if (value.Length == 0) _dugDescriptions.Remove(room);
			else _dugDescriptions[room] = value;
			Upsert(DigChange(room));
			return;
		}
		Upsert(new($"describe:{room}", MapChangeKind.Describe, room, Name(room), [Set(room, "DESCRIBE", value)]));
	}

	/// <summary>The description planned for <paramref name="room"/>, as it will be stored; null when the plan sets none.</summary>
	public string? PlannedDescription(string room) =>
		_dugDescriptions.GetValueOrDefault(room)
		?? _changes.FirstOrDefault(change => change.Key == $"describe:{room}")?.Steps[0].Expect;

	/// <summary>Renames <paramref name="room"/>, or a room the plan digs.</summary>
	public void Rename(string room, string name)
	{
		name = name.Trim();
		if (_dug.TryGetValue(room, out var dug))
		{
			Redig(room, dug with { Name = name });
			return;
		}
		var previous = Name(room);
		Upsert(new($"rename:{room}", MapChangeKind.Rename, room, previous, [new($"@name {room}={name}", $"name({room})", name)]) { Other = name });
	}

	/// <summary>
	/// Opens an exit <paramref name="names"/> from <paramref name="from"/>, a room on the map, to <paramref name="to"/>,
	/// and, when given, <paramref name="back"/> the other way. Either may be a room the plan digs. <paramref name="to"/>
	/// may be a room on another map or on none, by its dbref, called <paramref name="toName"/>.
	/// </summary>
	public string Join(string from, string to, string names, string? back, string? toName = null)
	{
		var key = $"join:{++_next}";
		if (Room(to) is null && toName is not null) _elsewhere[to] = toName;
		_joins[key] = (from, to, names.Trim(), string.IsNullOrWhiteSpace(back) ? null : back.Trim());
		Upsert(JoinChange(key));
		return key;
	}

	/// <summary>Gives the exit <paramref name="exit"/> new names: its name, then its aliases after <c>;</c>.</summary>
	public void RenameExit(string exit, string names)
	{
		var current = Map.Exits.FirstOrDefault(candidate => candidate.Id == exit);
		_changes.RemoveAll(change => change.Key == $"remove-exit:{exit}");
		Upsert(new($"rename-exit:{exit}", MapChangeKind.RenameExit, exit, current?.Name ?? exit,
			[new($"@name {exit}={names.Trim()}", $"name({exit})", First(names))])
		{ Other = First(names) });
	}

	/// <summary>Destroys the exit <paramref name="exit"/>.</summary>
	public void RemoveExit(string exit)
	{
		var current = Map.Exits.FirstOrDefault(candidate => candidate.Id == exit);
		_changes.RemoveAll(change => change.Key == $"rename-exit:{exit}");
		Upsert(new($"remove-exit:{exit}", MapChangeKind.RemoveExit, exit, current?.Name ?? exit,
			[new($"@destroy {exit}", $"or(not(valid({exit})),hasflag({exit},GOING))", "1")]));
	}

	/// <summary>Names level <paramref name="level"/> of the map; blank leaves it numbered.</summary>
	public void NameLevel(int level, string name)
	{
		var number = level.ToString(CultureInfo.InvariantCulture);
		Upsert(new($"level:{number}", MapChangeKind.NameLevel, number, name.Trim(),
			[Set(Map.Map.Id, $"MAP`LEVEL`{number}", name.Trim())])
		{ Other = number });
	}

	/// <summary>Renames the map object.</summary>
	public void RenameMap(string name)
	{
		var id = Map.Map.Id;
		Upsert(new("map:name", MapChangeKind.RenameMap, id, Map.Map.Name, [new($"@name {id}={name.Trim()}", $"name({id})", name.Trim())])
		{ Other = name.Trim() });
	}

	/// <summary>
	/// Zones the map to <paramref name="parent"/>, another map, so it shows inside it as a region; null takes it out of
	/// any. <paramref name="parentName"/> is how the review names it.
	/// </summary>
	public void MoveMap(string? parent, string? parentName)
	{
		var id = Map.Map.Id;
		Upsert(new("map:parent", MapChangeKind.MoveMap, id, Map.Map.Name,
			[new($"@chzone {id}={parent ?? "none"}", $"num(zone({id}))", parent ?? "#-1")])
		{ Other = parentName ?? "" });
	}

	/// <summary>The map's picture, by its address; empty takes it away.</summary>
	public void SetBackdrop(string url) => SetMap("backdrop", MapAttributes.Backdrop, url.Trim());

	/// <summary>The map's size: cells on a grid, the picture's pixels on a picture map.</summary>
	public void SetMapSize(double width, double height)
	{
		var id = Map.Map.Id;
		Upsert(new("map:size", MapChangeKind.MapSetting, id, MapAttributes.Size, [Set(id, MapAttributes.Width, Num(width)), Set(id, MapAttributes.Height, Num(height))])
		{ Other = $"{Num(width)} × {Num(height)}" });
	}

	/// <summary>The level the map opens on for someone who is not on it; null leaves it at the ground.</summary>
	public void SetDefaultLevel(int? level) =>
		SetMap("level", MapAttributes.Level, level?.ToString(CultureInfo.InvariantCulture) ?? "");

	/// <summary>
	/// How the legend draws <paramref name="type"/>: its label, a <c>tone()</c> colour's name and an icon name or a
	/// character or two. An empty value clears that part.
	/// </summary>
	public void SetLegend(string type, string label, string tone, string icon)
	{
		var id = Map.Map.Id;
		var key = type.Trim().ToUpperInvariant();
		Upsert(new($"legend:{key}", MapChangeKind.Legend, id, type.Trim(),
		[
			Set(id, $"{MapAttributes.Legend}`{key}`LABEL", label.Trim()),
			Set(id, $"{MapAttributes.Legend}`{key}`TONE", tone.Trim()),
			Set(id, $"{MapAttributes.Legend}`{key}`ICON", icon.Trim()),
		])
		{ Other = label.Trim() });
	}

	private void SetMap(string key, string attribute, string value)
	{
		var id = Map.Map.Id;
		Upsert(new($"map:{key}", MapChangeKind.MapSetting, id, attribute, [Set(id, attribute, value)]) { Other = value });
	}

	/// <summary>
	/// Makes the map a world map, which keeps its places itself (<c>@world/create</c>), of square places or of hexagons.
	/// </summary>
	public void CreateWorld(string space = MapSpaces.Square)
	{
		var id = Map.Map.Id;
		var command = space == MapSpaces.Square ? $"@world/create {id}" : $"@world/create {id}={space}";
		Upsert(new("world:create", MapChangeKind.CreateWorld, id, Map.Map.Name,
			[new(command, $"worldmap({id})", space)]));
	}

	/// <summary>
	/// Opens the direction exits that walk the map (world-grid's <c>+map/exits</c>), one for each way out of its places;
	/// the map must be a room.
	/// </summary>
	public void OpenWays()
	{
		var id = Map.Map.Id;
		Upsert(new("world:ways", MapChangeKind.OpenWays, id, Map.Map.Name,
			[new($"+map/exits {id}", $"gte(words(lexits({id})),words(worldmap({id},ways)))", "1")]));
	}

	/// <summary>Paints the places in the box from (x, y) to (x2, y2) on level z with <paramref name="type"/>; empty erases them.</summary>
	public void Paint(int x, int y, int x2, int y2, int z, string type)
	{
		var id = Map.Map.Id;
		var (left, right, bottom, top) = (Math.Min(x, x2), Math.Max(x, x2), Math.Min(y, y2), Math.Max(y, y2));
		var box = left == right && bottom == top ? $"{left},{bottom},{z}" : $"{left},{bottom},{z}:{right},{top}";
		Upsert(new($"paint:{++_next}", MapChangeKind.Paint, id, type.Trim(),
			[new($"@world/paint {id}/{box}={type.Trim()}", $"worldcell({id},{left},{bottom},{z})", type.Trim().ToLowerInvariant())])
		{ Other = box });
	}

	/// <summary>
	/// Sets side <paramref name="side"/> of place (x, y, z): <c>open</c>, <c>wall</c>, <c>oneway</c> or <c>lock</c> (which
	/// keeps the key it has), with the reason a refused mover is told. The check reads the way out through it, which is <c>none</c> or <c>closed</c> when the place
	/// beyond is unpainted or closed, whatever the side holds.
	/// </summary>
	public void Side(int x, int y, int z, string side, string state, string reason)
	{
		var id = Map.Map.Id;
		var at = Place(x, y, z);
		var value = reason.Trim().Length > 0 ? $"{state},{reason.Trim()}" : state;
		Upsert(new($"side:{at}/{side}", MapChangeKind.Side, id, $"{at}/{side}",
			[new($"@world/wall {id}/{at}/{side}={value}", $"t(match({state} none closed,worldway({id},{x},{y},{z},{side})))", "1")])
		{ Other = state });
	}

	/// <summary>
	/// On a graph map, makes the way <paramref name="names"/> out of place (x, y, z) to (toX, toY, toZ), and the way back
	/// named <paramref name="back"/> unless that is empty.
	/// </summary>
	public void Link(int x, int y, int z, int toX, int toY, int toZ, string names, string back)
	{
		var id = Map.Map.Id;
		var at = Place(x, y, z);
		var to = Place(toX, toY, toZ);
		var (given, returning) = (Names(names), Names(back));
		var way = Way(given);
		var value = returning.Length > 0 ? $"{to},{given},{returning}" : $"{to},{given}";
		var steps = new List<MapStep> { new($"@world/link {id}/{at}={value}", $"worldway({id},{x},{y},{z},{way},to)", to) };
		Upsert(new($"link:{at}/{way}", MapChangeKind.Link, id, at, steps) { Other = given.Split(';')[0] });
	}

	/// <summary>On a graph map, removes the way <paramref name="way"/> out of place (x, y, z); the way back stays.</summary>
	public void Unlink(int x, int y, int z, string way)
	{
		var id = Map.Map.Id;
		var at = Place(x, y, z);
		_changes.RemoveAll(change => change.Key == $"link:{at}/{way}");
		Upsert(new($"unlink:{at}/{way}", MapChangeKind.Unlink, id, $"{at}/{way}",
			[new($"@world/link {id}/{at}/{way}", $"worldway({id},{x},{y},{z},{way},to)", "")]));
	}

	/// <summary>A way's names as <c>@world/link</c> takes them: trimmed, <c>;</c> between them.</summary>
	private static string Names(string names) =>
		string.Join(";", names.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

	/// <summary>The name a place knows a way by: its first, in lower case, each run of spaces one <c>_</c>.</summary>
	private static string Way(string names) =>
		string.Join("_", names.Split(';')[0].ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

	/// <summary>Names place (x, y, z); empty takes its name away.</summary>
	public void NamePlace(int x, int y, int z, string name)
	{
		var id = Map.Map.Id;
		var at = Place(x, y, z);
		Upsert(new($"place-name:{at}", MapChangeKind.NamePlace, id, at,
			[new($"@world/place {id}/{at}={name.Trim()}", $"worldcell({id},{x},{y},{z},name)", name.Trim())])
		{ Other = name.Trim() });
	}

	/// <summary>Hides place (x, y, z) from whoever may not examine the map, or shows it again.</summary>
	public void HidePlace(int x, int y, int z, bool hidden)
	{
		var id = Map.Map.Id;
		var at = Place(x, y, z);
		var value = hidden ? "true" : "null";
		Upsert(new($"place-hidden:{at}", MapChangeKind.HidePlace, id, at,
			[new($"@world/data/merge {id}/{at}={{\"render\":{{\"hidden\":{value}}}}}",
				$"t(strmatch(worldcell({id},{x},{y},{z},data),*\"hidden\":true*))", hidden ? "1" : "0")])
		{ Other = hidden ? "1" : "" });
	}

	/// <summary>Places <paramref name="room"/> on place (x, y, z) to stay; empty takes the placed room off it.</summary>
	public void PlaceRoom(int x, int y, int z, string room)
	{
		var id = Map.Map.Id;
		var at = Place(x, y, z);
		Upsert(new($"place-room:{at}", MapChangeKind.PlaceRoom, id, at,
			[new($"@world/room {id}/{at}={room.Trim()}", $"num(worldcell({id},{x},{y},{z},room))", room.Trim().Length > 0 ? room.Trim().Split(':')[0] : "#-1")])
		{ Other = room.Trim() });
	}

	private static string Place(int x, int y, int z) => $"{x},{y},{z}";

	/// <summary>How many objects the plan makes, each counted against the builder's quota: a room per dig, an exit per way.</summary>
	public int Builds =>
		_dugExits.Values.Sum(exit => exit.From is null ? 1 : exit.Back is null ? 2 : 3) + _joins.Values.Sum(join => join.Back is null ? 1 : 2);

	/// <summary>Zones <paramref name="room"/> to this map.</summary>
	public void Add(MapRoomDto room)
	{
		_changes.RemoveAll(change => change.Key == $"remove:{room.Id}");
		Upsert(new($"add:{room.Id}", MapChangeKind.Add, room.Id, room.Name,
			[new($"@chzone {room.Id}={Map.Map.Id}", $"zone({room.Id})", Map.Map.Id)]));
		_added[room.Id] = room;
	}

	/// <summary>Takes <paramref name="room"/> off this map, or drops it from the plan when the plan digs or adds it.</summary>
	public void Remove(string room)
	{
		if (_dug.Remove(room))
		{
			_dugExits.Remove(room);
			_dugDescriptions.Remove(room);
			// A way to or from it would have nowhere to go.
			foreach (var (key, _) in _joins.Where(join => join.Value.From == room || join.Value.To == room).ToList())
			{
				_joins.Remove(key);
				_changes.RemoveAll(change => change.Key == key);
			}
			_changes.RemoveAll(change => change.Room == room);
			// A room dug from this one would have no room to be joined from.
			foreach (var (key, _) in _dugExits.Where(exit => exit.Value.From == room).ToList()) Remove(key);
			return;
		}
		_changes.RemoveAll(change => change.Room == room);
		if (_added.Remove(room)) return;
		_changes.Add(new($"remove:{room}", MapChangeKind.Remove, room, Name(room),
			[new($"@chzone {room}=none", $"zone({room})", "#-1")]));
	}

	/// <summary>
	/// Digs a room called <paramref name="name"/> at (x, y, z), with an exit <paramref name="exitTo"/> to it from
	/// <paramref name="from"/> and, when given, <paramref name="exitBack"/> back. With no <paramref name="from"/>, the
	/// first room of an empty map, it is dug with no exit. Returns the key the new room has in the plan.
	/// </summary>
	public string Dig(string name, double x, double y, int z, string? from, string exitTo, string? exitBack)
	{
		var key = $"new:{++_next}";
		_dug[key] = new(key, name, IsGraph ? [x, y] : [x, y, z], null) { Editable = true };
		_dugExits[key] = from is null
			? (null, "", null)
			: (from, exitTo, string.IsNullOrWhiteSpace(exitBack) ? null : exitBack.Trim());
		Upsert(DigChange(key));
		return key;
	}

	/// <summary>Drops the change <paramref name="key"/>, and with a dig the later changes to its room.</summary>
	public void Drop(string key)
	{
		if (_changes.FirstOrDefault(change => change.Key == key) is not { } change) return;
		if (change.Kind == MapChangeKind.Dig)
		{
			Remove(change.Room);
			return;
		}
		_changes.Remove(change);
		if (change.Kind == MapChangeKind.Join) _joins.Remove(change.Key);
		if (change.Kind == MapChangeKind.Add) _added.Remove(change.Room);
	}

	/// <summary>The map as it will be once every change has run: rooms moved, sized, added, dug or gone.</summary>
	public MapDto Preview()
	{
		var removed = _changes.Where(change => change.Kind == MapChangeKind.Remove).Select(change => change.Room).ToHashSet();
		var rooms = Map.Rooms.Concat(_added.Values).Where(room => !removed.Contains(room.Id)).Select(Planned).Concat(_dug.Values).ToList();
		var gone = _changes.Where(change => change.Kind == MapChangeKind.RemoveExit).Select(change => change.Room).ToHashSet();
		var renamed = _changes.Where(change => change.Kind == MapChangeKind.RenameExit)
			.ToDictionary(change => change.Room, change => change.Steps[0].Command[(change.Steps[0].Command.IndexOf('=') + 1)..]);
		var exits = Map.Exits.Where(exit => !removed.Contains(exit.Source) && !gone.Contains(exit.Id))
			.Select(exit => renamed.TryGetValue(exit.Id, out var names) ? exit with { Name = First(names), Aliases = Rest(names) } : exit)
			.ToList();
		foreach (var (key, (from, to, back)) in _dugExits)
		{
			if (from is null) continue;
			exits.Add(new($"{key}:to", First(to), Rest(to), from, key, null));
			if (back is not null) exits.Add(new($"{key}:back", First(back), Rest(back), key, from, null));
		}
		foreach (var (key, (from, to, names, back)) in _joins)
		{
			exits.Add(new($"{key}:to", First(names), Rest(names), from, to, null) { DestinationName = Name(to) });
			if (back is not null) exits.Add(new($"{key}:back", First(back), Rest(back), to, from, null));
		}
		var levels = new Dictionary<int, string>(Map.LevelNames);
		foreach (var change in _changes.Where(change => change.Kind == MapChangeKind.NameLevel))
		{
			var level = int.Parse(change.Room, CultureInfo.InvariantCulture);
			if (change.Name.Length == 0) levels.Remove(level);
			else levels[level] = change.Name;
		}
		return Map with
		{
			Map = Map.Map with
			{
				Name = Changed("map:name") is { } renaming ? Value(renaming) : Map.Map.Name,
				Parent = Changed("map:parent") is { } moving ? (Value(moving) == "#-1" ? null : Value(moving)) : Map.Map.Parent,
			},
			Rooms = rooms,
			Exits = exits,
			LevelNames = levels,
			Backdrop = Changed("map:backdrop") is { } backdrop ? NullIfEmpty(Value(backdrop)) : Map.Backdrop,
			Size = Changed("map:size") is { } size
				? [double.Parse(Value(size), CultureInfo.InvariantCulture), double.Parse(Value(size, 1), CultureInfo.InvariantCulture)]
				: Map.Size,
			Level = Changed("map:level") is { } opening
				? int.TryParse(Value(opening), CultureInfo.InvariantCulture, out var number) ? number : null
				: Map.Level,
			Legend = PlannedLegend(),
		};
	}

	/// <summary>
	/// What the builder should look at before applying: a room on the map, planned or not, that no exit leads into.
	/// Only rooms they may change are named, so a map they share does not nag them about rooms of others; a room standing on
	/// a place is reached by the map's own ways.
	/// </summary>
	public IReadOnlyList<MapRoomDto> NoWayIn()
	{
		var preview = Preview();
		var reached = preview.Exits.Where(exit => exit.Destination is not null && exit.Destination != exit.Source)
			.Select(exit => exit.Destination!).ToHashSet();
		return [.. preview.Rooms.Where(room => room.Editable && !room.OnPlace && !reached.Contains(room.Id) && room.Id != Map.Here)];
	}

	/// <summary>
	/// The exits of <paramref name="map"/> that lead to another room on it with no way back a player would look for: no
	/// exit back at all, or, for an exit named for a direction (<c>East</c>), none named for the opposite one (<c>West</c>).
	/// </summary>
	public static IReadOnlyList<MissingWayBack> OneWay(MapDto map)
	{
		// A truncated map may leave the way back out; it is not known to be missing.
		if (map.Truncated) return [];
		var rooms = map.Rooms.Select(room => room.Id).ToHashSet();
		var missing = new List<MissingWayBack>();
		foreach (var exit in map.Exits.Where(exit => exit.Destination is { } to && to != exit.Source && rooms.Contains(to)))
		{
			var backs = map.Exits.Where(back => back.Source == exit.Destination && back.Destination == exit.Source).ToList();
			if (backs.Count == 0) missing.Add(new(exit, null));
			else if (MapLayout.Direction(exit) is (var x, var y, var z) && !backs.Any(back => Names(back).Any(name => MapLayout.Direction(back with { Name = name, Aliases = [] }) == (-x, -y, -z))))
				missing.Add(new(exit, First(WayBackNames(exit))));
		}
		return missing;
	}

	private static IEnumerable<string> Names(MapExitDto exit) => [exit.Name, .. exit.Aliases];

	/// <summary>
	/// The names the way back through <paramref name="exit"/> takes: the opposite direction, <c>West;w</c> for
	/// <c>East</c>, when one of its names is a compass direction; empty when none is.
	/// </summary>
	public static string WayBackNames(MapExitDto exit) =>
		MapLayout.Direction(exit) is (var dx, var dy, var dz) && DirectionNames(dx, dy, dz) is { Back: var back } ? back : "";

	/// <summary>
	/// The exit names a dig in direction (dx, dy, dz) gets: <c>East;e</c> there and <c>West;w</c> back. Null when the
	/// step is no compass direction.
	/// </summary>
	public static (string To, string Back)? DirectionNames(int dx, int dy, int dz) => (Math.Sign(dx), Math.Sign(dy), Math.Sign(dz)) switch
	{
		(0, 1, 0) => ("North;n", "South;s"),
		(0, -1, 0) => ("South;s", "North;n"),
		(1, 0, 0) => ("East;e", "West;w"),
		(-1, 0, 0) => ("West;w", "East;e"),
		(1, 1, 0) => ("Northeast;ne", "Southwest;sw"),
		(-1, -1, 0) => ("Southwest;sw", "Northeast;ne"),
		(-1, 1, 0) => ("Northwest;nw", "Southeast;se"),
		(1, -1, 0) => ("Southeast;se", "Northwest;nw"),
		(0, 0, 1) => ("Up;u", "Down;d"),
		(0, 0, -1) => ("Down;d", "Up;u"),
		_ => null,
	};

	/// <summary>
	/// Why <paramref name="names"/> cannot name an exit, or null when it can: it is empty, or one of its names is
	/// <c>in</c> or <c>i</c> (<c>i</c> is inventory), or holds a character the command line would split on.
	/// </summary>
	public static string? ExitNameProblem(string? names)
	{
		if (string.IsNullOrWhiteSpace(names)) return "empty";
		var parts = names.Split(';', StringSplitOptions.TrimEntries);
		if (parts.Any(part => part.Length == 0)) return "empty";
		if (parts.Any(part => part.Equals("in", StringComparison.OrdinalIgnoreCase) || part.Equals("i", StringComparison.OrdinalIgnoreCase))) return "in";
		return parts.Any(part => part.IndexOfAny([',', '=', '[', ']', '%', '\\']) >= 0) ? "character" : null;
	}

	/// <summary>
	/// Why <paramref name="name"/> cannot name a dug room, or null when it can: it is empty, or holds a character
	/// <c>@dig</c> would read as more than a name.
	/// </summary>
	public static string? RoomNameProblem(string? name)
	{
		if (string.IsNullOrWhiteSpace(name)) return "empty";
		return name.IndexOfAny(['=', '[', ']', '%', '\\']) >= 0 ? "character" : null;
	}

	/// <summary>Whether <paramref name="value"/> is a dbref or an objid (<c>#84:1793148715366</c>), as a dig's first step must give.</summary>
	public static bool IsDbref(string? value) => value is not null && DbrefPattern().IsMatch(value.Trim());

	/// <summary>
	/// Whether <paramref name="step"/> worked, by what its check read: a dbref where it expects one, an error where it
	/// expects <c>#-1</c> (no zone), and otherwise the value it expects, an objid read as its dbref.
	/// </summary>
	public static bool Worked(MapStep step, string? read)
	{
		var value = read?.Trim() ?? "";
		return step.Expect switch
		{
			null => IsDbref(value),
			"#-1" => value.StartsWith("#-1", StringComparison.Ordinal),
			{ } expect => (IsDbref(value) ? value.Split(':')[0] : value) == expect,
		};
	}

	/// <summary>
	/// <paramref name="text"/> with each dug room it names by token (<c>{new:1}</c>) given as the objid in
	/// <paramref name="made"/>, once that room exists.
	/// </summary>
	public static string Resolve(string text, IReadOnlyDictionary<string, string> made) =>
		TokenPattern().Replace(text, match => made.GetValueOrDefault(match.Groups[1].Value) ?? match.Value);

	/// <summary><paramref name="command"/> as the review shows it: a dug room's token as its name in angle brackets.</summary>
	public string Show(string command) =>
		TokenPattern().Replace(command, match => _dug.TryGetValue(match.Groups[1].Value, out var room) ? $"<{room.Name}>" : match.Value);

	private static string Token(string key) => $"{{{key}}}";

	[GeneratedRegex(@"^#\d+(:\d+)?$")]
	private static partial Regex DbrefPattern();

	[GeneratedRegex(@"\{(new:\d+)\}")]
	private static partial Regex TokenPattern();

	private MapChange DigChange(string key)
	{
		var room = _dug[key];
		var (from, to, back) = _dugExits[key];
		var position = room.Position!;
		var self = Token(key);
		var steps = new List<MapStep>
		{
			new($"@dig {room.Name}", "%>", null),
			new($"@chzone {self}={Map.Map.Id}", $"zone({self})", Map.Map.Id),
		};
		if (IsGraph)
		{
			steps.Add(Set(self, "MAP`POS`X", Num(position[0])));
			steps.Add(Set(self, "MAP`POS`Y", Num(position[1])));
		}
		else
		{
			steps.Add(Set(self, "MAP`COORDS`X", Num(position[0])));
			steps.Add(Set(self, "MAP`COORDS`Y", Num(position[1])));
			if ((int)position[2] is not 0) steps.Add(Set(self, "MAP`COORDS`Z", Num(position[2])));
		}
		if (room.Size is [var width, var height, ..])
		{
			steps.Add(Set(self, "MAP`SIZE`W", Num(width)));
			steps.Add(Set(self, "MAP`SIZE`H", Num(height)));
		}
		if (from is not null)
		{
			var source = _dug.ContainsKey(from) ? Token(from) : from;
			// @open gives the exit it made even when linking it, or opening the way back, failed: ask for both.
			var linked = $"strmatch(num(loc(%>)),num({self}))";
			var check = string.IsNullOrEmpty(back) ? linked : $"and({linked},strmatch(num(loc(lexits({self}))),num({source})))";
			steps.Add(new($"@open {to}={self},{back ?? ""},{source}", check, "1"));
		}
		if (_dugDescriptions.TryGetValue(key, out var description)) steps.Add(Set(self, "DESCRIBE", description));
		return new(key, MapChangeKind.Dig, key, room.Name, steps);
	}

	/// <summary>
	/// A join's <c>@open</c>, checked as a dig's is: the exit it made leads to the room, and when there is a way back, the
	/// newest exit in that room leads back.
	/// </summary>
	private MapChange JoinChange(string key)
	{
		var (from, to, names, back) = _joins[key];
		var source = _dug.ContainsKey(from) ? Token(from) : from;
		var destination = _dug.ContainsKey(to) ? Token(to) : to;
		var linked = $"strmatch(num(loc(%>)),num({destination}))";
		var check = back is null ? linked : $"and({linked},strmatch(num(loc(first(lexits({destination})))),num({source})))";
		return new(key, MapChangeKind.Join, from, Name(from), [new($"@open {names}={destination},{back ?? ""},{source}", check, "1")])
		{
			Other = Name(to),
		};
	}

	private void Redig(string key, MapRoomDto room)
	{
		_dug[key] = room;
		Upsert(DigChange(key));
	}

	/// <summary>Adds <paramref name="change"/>, in place of an earlier one with its key, so a room moved twice moves once.</summary>
	private void Upsert(MapChange change)
	{
		var at = _changes.FindIndex(existing => existing.Key == change.Key);
		if (at >= 0) _changes[at] = change;
		else _changes.Add(change);
	}

	private MapRoomDto Planned(MapRoomDto room)
	{
		foreach (var change in _changes.Where(change => change.Room == room.Id))
		{
			room = change.Kind switch
			{
				MapChangeKind.Rename => room with { Name = change.Steps[0].Expect! },
				MapChangeKind.Place => room with { Position = Values(change, IsGraph ? ["MAP`POS`X", "MAP`POS`Y"] : ["MAP`COORDS`X", "MAP`COORDS`Y", "MAP`COORDS`Z"]) },
				MapChangeKind.Size => room with { Size = Values(change, ["MAP`SIZE`W", "MAP`SIZE`H"]) is [> 0, > 0, ..] size ? size : null },
				_ => room,
			};
		}
		return room;
	}

	/// <summary>The numbers <paramref name="change"/> sets, attribute by attribute: one it leaves out is 0, and with none, null.</summary>
	private static double[]? Values(MapChange change, string[] attributes)
	{
		var set = change.Steps.Select(step => step.Command).Where(command => command.StartsWith('&')).ToList();
		var values = attributes.Select(attribute => set.FirstOrDefault(command => command.StartsWith($"&{attribute} ", StringComparison.Ordinal)))
			.Select(command => command?[(command.IndexOf('=') + 1)..])
			.Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0)
			.ToArray();
		return set.All(command => command.EndsWith('=')) ? null : values;
	}

	private MapChange? Changed(string key) => _changes.FirstOrDefault(change => change.Key == key);

	private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

	/// <summary>What step <paramref name="step"/> of <paramref name="change"/> sets.</summary>
	private static string Value(MapChange change, int step = 0) => change.Steps[step].Expect ?? "";

	/// <summary>
	/// The legend with each planned entry in place of the stored one. An entry with nothing set leaves the type to the
	/// maps this one is zoned to: their entry shows, and the type is left out when none names it.
	/// </summary>
	private IReadOnlyList<MapLegendDto> PlannedLegend()
	{
		var legend = Map.Legend.ToDictionary(entry => entry.Type.ToUpperInvariant(), StringComparer.Ordinal);
		foreach (var change in _changes.Where(change => change.Kind == MapChangeKind.Legend))
		{
			var (label, tone, icon) = (Value(change), Value(change, 1), Value(change, 2));
			var type = change.Name.ToUpperInvariant();
			if (label.Length == 0 && tone.Length == 0 && icon.Length == 0)
			{
				if (legend.GetValueOrDefault(type) is { } stored && (stored.Inherited ? stored : stored.Parent) is { } above) legend[type] = above;
				else legend.Remove(type);
			}
			else legend[type] = new MapLegendDto(legend.GetValueOrDefault(type)?.Type ?? change.Name.ToLowerInvariant(),
				label.Length > 0 ? label : change.Name, NullIfEmpty(tone.ToLowerInvariant()), NullIfEmpty(icon));
		}
		return [.. legend.Values];
	}

	private MapRoomDto? Room(string id) =>
		Map.Rooms.FirstOrDefault(room => room.Id == id) ?? _added.GetValueOrDefault(id) ?? _dug.GetValueOrDefault(id);

	private string Name(string id) => Room(id)?.Name ?? _elsewhere.GetValueOrDefault(id) ?? id;

	private static MapStep Set(string room, string attribute, string value) =>
		new($"&{attribute} {room}={value}", $"get({room}/{attribute})", value);

	private static string First(string names) => names.Split(';', StringSplitOptions.TrimEntries)[0];

	private static IReadOnlyList<string> Rest(string names) => [.. names.Split(';', StringSplitOptions.TrimEntries).Skip(1)];

	private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

	/// <summary>A description as one command line takes it: trimmed, each line break as <c>%r</c>.</summary>
	private static string Description(string text) =>
		string.Join("%r", text.Replace("\r\n", "\n").Replace('\r', '\n').Trim().Split('\n').Select(line => line.TrimEnd()));
}

/// <summary>How one change of an applied plan went.</summary>
/// <param name="Error">What the game said when a step failed.</param>
/// <summary>
/// An exit with no way back a player would look for: none at all when <paramref name="Expected"/> is null, else none
/// named <paramref name="Expected"/>, the opposite of the exit's direction.
/// </summary>
public sealed record MissingWayBack(MapExitDto Exit, string? Expected);

public sealed record MapApplied(string Title, MapAppliedState State, string? Error);

public enum MapAppliedState
{
	Done,
	Failed,
	NotRun,
}

/// <summary>The attributes on a map object that say how the World map draws it.</summary>
public static class MapAttributes
{
	public const string Backdrop = "MAP`BACKDROP";
	public const string Size = "MAP`SIZE";
	public const string Width = "MAP`SIZE`W";
	public const string Height = "MAP`SIZE`H";
	public const string Level = "MAP`LEVEL";
	public const string Legend = "MAP`LEGEND";
}
