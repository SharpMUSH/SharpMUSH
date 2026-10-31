using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World.Functions;

/// <summary>
/// The <c>world…</c> functions, named for <c>@world</c>: <c>worldcell()</c> reads a place, <c>worldway()</c> a way out
/// of one, and <c>worlddraw()</c> draws the places around one as text. They read only. A place whose data hides it
/// reads as bare terrain, and its data shows only <c>render</c> and <c>info</c>, to whoever may not examine the map.
/// <c>worldroom()</c> gives the room standing on a place, lending it one, and <c>worldplace()</c> the place a room
/// stands on.
/// </summary>
public static class WorldFunctions
{
	public const string NotAWorldMap = "#-1 NOT A WORLD MAP";
	public const string BadPlace = "#-1 BAD PLACE";
	public const string BadField = "#-1 BAD FIELD";
	public const string BadDirection = "#-1 BAD DIRECTION";
	public const string BadRadius = "#-1 BAD RADIUS";
	public const string NotARoom = "#-1 NOT A ROOM";

	/// <summary>The furthest <c>worlddraw()</c> reaches from its centre.</summary>
	public const int MaxRadius = 20;

	private sealed record Reading(AnySharpObject Map, WorldMapRecord Record, WorldStore Store, WorldLegend Legend, bool Examine);

	private static string Arg(IMUSHCodeParser parser, int index) =>
		parser.CurrentState.Arguments.TryGetValue(index.ToString(), out var arg) ? arg.Message?.ToPlainText().Trim() ?? "" : "";

	private static async ValueTask<CallState> ReadAsync(IMUSHCodeParser parser, Func<Reading, ValueTask<CallState>> read)
	{
		var executor = await WorldMaps.ExecutorAsync(parser);
		var locate = parser.ServiceProvider.GetRequiredService<ILocateService>();
		return await locate.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, Arg(parser, 0), LocateFlags.All,
			async map =>
			{
				var store = parser.ServiceProvider.GetRequiredService<WorldStore>();
				if (store.Map(map.Object().DBRef) is not { } record) return new CallState(NotAWorldMap);
				var examine = await parser.ServiceProvider.GetRequiredService<IPermissionService>().CanExamine(executor, map);
				var legend = new WorldLegend(parser.ServiceProvider.GetRequiredService<IAttributeService>(), map);
				return await read(new Reading(map, record, store, legend, examine));
			});
	}

	/// <summary>The place at arguments 1 to 3; z is 0 when argument 3 is empty or missing.</summary>
	private static WorldPosition? Position(IMUSHCodeParser parser) =>
		WorldPosition.Parse(Arg(parser, 3) is { Length: > 0 } z ? $"{Arg(parser, 1)},{Arg(parser, 2)},{z}" : $"{Arg(parser, 1)},{Arg(parser, 2)}");

	/// <summary>
	/// worldcell(&lt;map&gt;, &lt;x&gt;, &lt;y&gt;[, &lt;z&gt;[, &lt;field&gt;]]): the place's <c>type</c> (the default),
	/// <c>label</c>, <c>name</c>, <c>data</c>, <c>closed</c> (1 or 0), <c>reason</c>, <c>id</c>, <c>room</c> (the room
	/// standing there, lending none) or <c>permanent</c> (1 when that room was placed there to stay).
	/// </summary>
	[SharpFunction(Name = "worldcell", MinArgs = 3, MaxArgs = 5, Flags = FunctionFlags.Regular,
		ParameterNames = ["map", "x", "y", "z", "field"])]
	public static ValueTask<CallState> WorldCell(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ReadAsync(parser, async reading =>
		{
			if (Position(parser) is not { } at) return new CallState(BadPlace);
			var field = Arg(parser, 4) is { Length: > 0 } given ? given.ToLowerInvariant() : "type";
			var dbref = reading.Map.Object().DBRef;
			var type = reading.Record.Type(reading.Store.Cell(dbref, at.X, at.Y, at.Z).Terrain);
			var place = reading.Store.Place(dbref, at.X, at.Y, at.Z);
			// A hidden place reads as bare terrain: no name, data or room of its own.
			var hidden = !reading.Examine && WorldPlaceData.Hidden(WorldPlaceData.Parse(place?.Data));
			if (hidden) place = null;
			var terrain = type is null ? null : await reading.Legend.TerrainAsync(type);
			var rooms = parser.ServiceProvider.GetRequiredService<WorldRooms>();
			var standing = type is null || hidden || field is not ("room" or "permanent") ? null : await rooms.StandingAsync(dbref, at);
			return field switch
			{
				"type" => new CallState(type ?? ""),
				"label" => new CallState(terrain?.Label ?? ""),
				"name" => new CallState(place?.Name ?? ""),
				"data" => new CallState(WorldPlaceData.Visible(place?.Data, reading.Examine) ?? ""),
				"closed" => new CallState(terrain?.Closed is not null),
				"reason" => new CallState(terrain?.Closed ?? ""),
				"id" => new CallState(type is null ? "" : WorldPlaceId.Of(dbref, at.X, at.Y, at.Z)),
				"room" => new CallState(standing is { } held ? $"#{held.Number}" : ""),
				"permanent" => new CallState(standing is { } room && reading.Store.Room(room) is { Permanent: true }),
				_ => new CallState(BadField),
			};
		});

	/// <summary>
	/// worldmap(&lt;map&gt;[, &lt;field&gt;]): a world map's <c>space</c> (the default: <c>square</c> or <c>hex</c>),
	/// <c>sides</c> (its places' sides, by letter), <c>ways</c> (the directions worldway() takes there), <c>levels</c> (the levels painted, lowest first) or <c>box</c> (the
	/// painted box as <c>left bottom right top</c>, empty before anything is painted).
	/// </summary>
	[SharpFunction(Name = "worldmap", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["map", "field"])]
	public static ValueTask<CallState> WorldMap(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ReadAsync(parser, reading =>
		{
			var record = reading.Record;
			return ValueTask.FromResult((Arg(parser, 1) is { Length: > 0 } given ? given.ToLowerInvariant() : "space") switch
			{
				"space" => new CallState(record.Space),
				"sides" => new CallState(string.Join(" ", WorldGrid.Sides(record.Space).Select(WorldGrid.Letter))),
				"ways" => new CallState(string.Join(" ", WorldWays.Directions(record.Space))),
				"levels" => new CallState(string.Join(" ", record.Levels)),
				"box" => new CallState(record.Painted ? $"{record.Left} {record.Bottom} {record.Right} {record.Top}" : ""),
				_ => new CallState(BadField),
			});
		});

	/// <summary>
	/// worldway(&lt;map&gt;, &lt;x&gt;, &lt;y&gt;, &lt;z&gt;, &lt;direction&gt;[, &lt;field&gt;[, &lt;mover&gt;]]): the way out of a
	/// place toward one of its sides, or on a square map a diagonal: its <c>state</c> (the default: open, oneway, lock, closed, wall or none),
	/// <c>reason</c>, or <c>to</c>, the place it leads to. Given a mover, each lock on the way is evaluated for them, as an
	/// exit's lock is, with the map as the locked object: a lock they pass is open. Naming a mover other than yourself
	/// needs examine of the map.
	/// </summary>
	[SharpFunction(Name = "worldway", MinArgs = 5, MaxArgs = 7, Flags = FunctionFlags.Regular,
		ParameterNames = ["map", "x", "y", "z", "direction", "field", "mover"])]
	public static ValueTask<CallState> WorldWay(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ReadAsync(parser, async reading =>
		{
			if (Position(parser) is not { } at) return new CallState(BadPlace);
			if (WorldWays.ParseDirection(Arg(parser, 4), reading.Record.Space) is not { } direction) return new CallState(BadDirection);
			var field = Arg(parser, 5) is { Length: > 0 } given ? given.ToLowerInvariant() : "state";
			if (field is not ("state" or "reason" or "to")) return new CallState(BadField);

			Func<string, ValueTask<bool>>? unlock = null;
			if (Arg(parser, 6) is { Length: > 0 } moverName)
			{
				var executor = await WorldMaps.ExecutorAsync(parser);
				var locate = parser.ServiceProvider.GetRequiredService<ILocateService>();
				if (await locate.Locate(parser, executor, executor, moverName, LocateFlags.All) is not AnySharpObject mover)
					return new CallState(ErrorMessages.Returns.NoMatch);
				if (mover.Object().DBRef.Number != executor.Object().DBRef.Number && !reading.Examine)
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				unlock = Unlock(parser, reading.Map, mover);
			}

			var way = await WorldWays.WayAsync(reading.Store, reading.Record, reading.Legend, reading.Map.Object().DBRef, reading.Examine, at,
				direction, unlock);
			return field switch
			{
				"state" => new CallState(way.StateWord),
				"reason" => new CallState(way.Reason ?? ""),
				_ => new CallState(way.To.ToString()),
			};
		});

	/// <summary>
	/// Whether <paramref name="mover"/> passes a lock key, with <paramref name="map"/> as the locked object. A side's key
	/// was bound when it was set; a legend's key is typed into an attribute, so its names are found as the map finds them.
	/// An empty key or one that names nothing lets nobody through.
	/// </summary>
	private static Func<string, ValueTask<bool>> Unlock(IMUSHCodeParser parser, AnySharpObject map, AnySharpObject mover)
	{
		var keys = parser.ServiceProvider.GetRequiredService<IBooleanExpressionParser>();
		var locks = parser.ServiceProvider.GetRequiredService<ILockService>();
		return async key =>
		{
			if (string.IsNullOrWhiteSpace(key)) return false;
			if (keys.IsBound(key)) return await locks.Evaluate(key, map, mover);
			return await keys.BindAsync(key, map) is string bound && await locks.Evaluate(bound, map, mover);
		};
	}

	/// <summary>
	/// worlddraw(&lt;map&gt;, &lt;x&gt;, &lt;y&gt;[, &lt;z&gt;[, &lt;radius&gt;[, &lt;mark&gt;]]]): the places within
	/// &lt;radius&gt; (3 by default, at most 20) of a place, one character each and north up, a row a line. A place
	/// with nothing painted is a space; &lt;mark&gt;, when given, stands in the centre. A hex map draws each column two
	/// characters apart and each column half a row above the one to its west, so a place's row takes two lines.
	/// </summary>
	[SharpFunction(Name = "worlddraw", MinArgs = 3, MaxArgs = 6, Flags = FunctionFlags.Regular,
		ParameterNames = ["map", "x", "y", "z", "radius", "mark"])]
	public static ValueTask<CallState> WorldDraw(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ReadAsync(parser, async reading =>
		{
			if (Position(parser) is not { } at) return new CallState(BadPlace);
			var radius = 3;
			if (Arg(parser, 4) is { Length: > 0 } given && (!int.TryParse(given, out radius) || radius is < 0 or > MaxRadius))
				return new CallState(BadRadius);
			var mark = parser.CurrentState.Arguments.TryGetValue("5", out var markArg) ? markArg.Message?.ToPlainText() ?? "" : "";

			var dbref = reading.Map.Object().DBRef;
			var window = reading.Store.Window(dbref, at.Z, at.X - radius, at.Y - radius, at.X + radius, at.Y + radius);
			if (reading.Record.Space == MapSpaces.Hex) return new CallState(await HexDrawingAsync(reading, window, at, radius, mark));
			var drawing = new StringBuilder();
			for (var y = at.Y + radius; y >= at.Y - radius; y--)
			{
				if (y != at.Y + radius) drawing.Append('\n');
				for (var x = at.X - radius; x <= at.X + radius; x++)
				{
					if (x == at.X && y == at.Y && mark.Length > 0)
					{
						drawing.Append(mark);
						continue;
					}
					drawing.Append(await GlyphAsync(reading, window, x, y));
				}
			}
			return new CallState(drawing.ToString());
		});

	/// <summary>
	/// The hex places within <paramref name="radius"/> steps of <paramref name="at"/>: column x on character 2·(x − at.X + radius),
	/// and place (x, y) on line 2·radius − (2·(y − at.Y) + x − at.X), so north is up and each column sits half a row
	/// above the one to its left.
	/// </summary>
	private static async ValueTask<string> HexDrawingAsync(Reading reading, WorldWindowRead? window, WorldPosition at, int radius, string mark)
	{
		// A slot per character position, so a glyph of several UTF-16 units never shifts or splits another.
		var lines = Enumerable.Range(0, 4 * radius + 1).Select(_ => Enumerable.Repeat(" ", 4 * radius + 1).ToArray()).ToArray();
		for (var dx = -radius; dx <= radius; dx++)
		{
			for (var dy = Math.Max(-radius, -dx - radius); dy <= Math.Min(radius, -dx + radius); dy++)
			{
				var (line, column) = (2 * radius - (2 * dy + dx), 2 * (dx + radius));
				lines[line][column] = dx == 0 && dy == 0 && mark.Length > 0 ? mark : await GlyphAsync(reading, window, at.X + dx, at.Y + dy);
			}
		}
		return string.Join("\n", lines.Select(line => string.Concat(line)));
	}

	private static async ValueTask<string> GlyphAsync(Reading reading, WorldWindowRead? window, int x, int y)
	{
		if (window is null || !window.Cells.TryGetValue((x, y), out var cell) || reading.Record.Type(cell.Terrain) is not { } type) return " ";
		return (await reading.Legend.TerrainAsync(type)).Glyph;
	}

	/// <summary>
	/// worldroom(&lt;map&gt;, &lt;x&gt;, &lt;y&gt;[, &lt;z&gt;]), or worldroom(&lt;map&gt;, &lt;x&gt;,&lt;y&gt;,&lt;z&gt;) with the
	/// place in one argument as <c>worldway()</c>'s <c>to</c> gives it: the room standing on a place, lending it one
	/// when none does. Needs control of the map.
	/// </summary>
	[SharpFunction(Name = "worldroom", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		ParameterNames = ["map", "x", "y", "z"])]
	public static ValueTask<CallState> WorldRoom(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ReadAsync(parser, async reading =>
		{
			var executor = await WorldMaps.ExecutorAsync(parser);
			if (!await parser.ServiceProvider.GetRequiredService<IPermissionService>().Controls(executor, reading.Map))
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			var at = parser.CurrentState.Arguments.Count == 2 ? WorldPosition.Parse(Arg(parser, 1)) : Position(parser);
			if (at is not { } place) return new CallState(BadPlace);
			return await parser.ServiceProvider.GetRequiredService<WorldRooms>().RoomAsync(reading.Map, reading.Record, place) switch
			{
				DBRef room => new CallState($"#{room.Number}"),
				Error<string> error => new CallState(error.Value),
			};
		});

	/// <summary>
	/// worldplace(&lt;room&gt;[, &lt;field&gt;]): where a room stands: <c>coords</c> (the default, as <c>x,y,z</c>),
	/// <c>x</c>, <c>y</c>, <c>z</c>, <c>map</c>, <c>id</c> or <c>permanent</c> (1 when it was placed there to stay). Empty
	/// for a room on no world map. Needs to examine the room, or be in it.
	/// </summary>
	[SharpFunction(Name = "worldplace", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular,
		ParameterNames = ["room", "field"])]
	public static async ValueTask<CallState> WorldPlace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await WorldMaps.ExecutorAsync(parser);
		var locate = parser.ServiceProvider.GetRequiredService<ILocateService>();
		return await locate.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, Arg(parser, 0), LocateFlags.All,
			async room =>
			{
				if (!room.IsRoom) return new CallState(NotARoom);
				var field = Arg(parser, 1) is { Length: > 0 } given ? given.ToLowerInvariant() : "coords";
				if (field is not ("coords" or "x" or "y" or "z" or "map" or "id" or "permanent")) return new CallState(BadField);
				var dbref = room.Object().DBRef;
				var inside = (await executor.Where()).Object().DBRef.Number == dbref.Number;
				if (!inside && !await parser.ServiceProvider.GetRequiredService<IPermissionService>().CanExamine(executor, room))
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				if (parser.ServiceProvider.GetRequiredService<WorldStore>().Room(dbref) is not { } stands) return new CallState("");
				var map = new DBRef((int)stands.Map, stands.MapCreated);
				var at = new WorldPosition(stands.X, stands.Y, stands.Z);
				return field switch
				{
					"x" => new CallState(at.X.ToString()),
					"y" => new CallState(at.Y.ToString()),
					"z" => new CallState(at.Z.ToString()),
					"map" => new CallState(new DBRef(map.Number).ToString()),
					"id" => new CallState(WorldPlaceId.Of(map, at.X, at.Y, at.Z)),
					"permanent" => new CallState(stands.Permanent),
					_ => new CallState(at.ToString()),
				};
			});
	}
}
