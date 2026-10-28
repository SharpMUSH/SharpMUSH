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
public sealed record MapChange(string Key, MapChangeKind Kind, string Room, string Name, IReadOnlyList<MapStep> Steps);

public enum MapChangeKind
{
	Place,
	Size,
	Add,
	Remove,
	Dig,
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
	private readonly Dictionary<string, (string From, string To, string? Back)> _dugExits = new();
	private readonly Dictionary<string, MapRoomDto> _added = new();
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
			Redig(room, dug with { Size = width == 1 && height == 1 ? null : [width, height] });
			return;
		}
		var clear = width == 1 && height == 1;
		Upsert(new($"size:{room}", MapChangeKind.Size, room, Name(room),
		[
			Set(room, "MAP`SIZE`W", clear ? "" : Num(width)),
			Set(room, "MAP`SIZE`H", clear ? "" : Num(height)),
		]));
	}

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
	/// <paramref name="from"/> and, when given, <paramref name="exitBack"/> back. Returns the key the new room has
	/// in the plan.
	/// </summary>
	public string Dig(string name, double x, double y, int z, string from, string exitTo, string? exitBack)
	{
		var key = $"new:{++_next}";
		_dug[key] = new(key, name, IsGraph ? [x, y] : [x, y, z], null) { Editable = true };
		_dugExits[key] = (from, exitTo, string.IsNullOrWhiteSpace(exitBack) ? null : exitBack.Trim());
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
		if (change.Kind == MapChangeKind.Add) _added.Remove(change.Room);
	}

	/// <summary>The map as it will be once every change has run: rooms moved, sized, added, dug or gone.</summary>
	public MapDto Preview()
	{
		var removed = _changes.Where(change => change.Kind == MapChangeKind.Remove).Select(change => change.Room).ToHashSet();
		var rooms = Map.Rooms.Concat(_added.Values).Where(room => !removed.Contains(room.Id)).Select(Planned).Concat(_dug.Values).ToList();
		var exits = Map.Exits.Where(exit => !removed.Contains(exit.Source)).ToList();
		foreach (var (key, (from, to, back)) in _dugExits)
		{
			exits.Add(new($"{key}:to", First(to), Rest(to), from, key, null));
			if (back is not null) exits.Add(new($"{key}:back", First(back), Rest(back), key, from, null));
		}
		return Map with { Rooms = rooms, Exits = exits };
	}

	/// <summary>
	/// What the builder should look at before applying: a room on the map, planned or not, that no exit leads into.
	/// Only rooms they may change are named, so a map they share does not nag them about rooms of others.
	/// </summary>
	public IReadOnlyList<MapRoomDto> NoWayIn()
	{
		var preview = Preview();
		var reached = preview.Exits.Where(exit => exit.Destination is not null && exit.Destination != exit.Source)
			.Select(exit => exit.Destination!).ToHashSet();
		return [.. preview.Rooms.Where(room => room.Editable && !reached.Contains(room.Id) && room.Id != Map.Here)];
	}

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
			var expect => (IsDbref(value) ? value.Split(':')[0] : value) == expect,
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
		var source = _dug.ContainsKey(from) ? Token(from) : from;
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
			if (position[2] != 0) steps.Add(Set(self, "MAP`COORDS`Z", Num(position[2])));
		}
		if (room.Size is [var width, var height, ..])
		{
			steps.Add(Set(self, "MAP`SIZE`W", Num(width)));
			steps.Add(Set(self, "MAP`SIZE`H", Num(height)));
		}
		steps.Add(new($"@open {to}={self},{back ?? ""},{source}", "%>", null));
		return new(key, MapChangeKind.Dig, key, room.Name, steps);
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
		return values.All(value => value == 0) && set.All(command => command.EndsWith('=')) ? null : values;
	}

	private MapRoomDto? Room(string id) =>
		Map.Rooms.FirstOrDefault(room => room.Id == id) ?? _added.GetValueOrDefault(id) ?? _dug.GetValueOrDefault(id);

	private string Name(string id) => Room(id)?.Name ?? id;

	private static MapStep Set(string room, string attribute, string value) =>
		new($"&{attribute} {room}={value}", $"get({room}/{attribute})", value);

	private static string First(string names) => names.Split(';', StringSplitOptions.TrimEntries)[0];

	private static IReadOnlyList<string> Rest(string names) => [.. names.Split(';', StringSplitOptions.TrimEntries).Skip(1)];

	private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>How one change of an applied plan went.</summary>
/// <param name="Error">What the game said when a step failed.</param>
public sealed record MapApplied(string Title, MapAppliedState State, string? Error);

public enum MapAppliedState
{
	Done,
	Failed,
	NotRun,
}
