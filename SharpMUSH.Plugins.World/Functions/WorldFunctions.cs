using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World.Functions;

/// <summary>
/// The <c>world…</c> functions, named for <c>@world</c>: <c>worldcell()</c> reads a place, <c>worldway()</c> a way out
/// of one, and <c>worlddraw()</c> draws the places around one as text. They read only. A place whose data hides it
/// reads as nothing, and its data shows only <c>render</c> and <c>info</c>, to whoever may not examine the map.
/// </summary>
public static class WorldFunctions
{
	public const string NotAWorldMap = "#-1 NOT A WORLD MAP";
	public const string BadPlace = "#-1 BAD PLACE";
	public const string BadField = "#-1 BAD FIELD";
	public const string BadDirection = "#-1 BAD DIRECTION";
	public const string BadRadius = "#-1 BAD RADIUS";

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
	/// <c>label</c>, <c>name</c>, <c>data</c>, <c>closed</c> (1 or 0), <c>reason</c> or <c>id</c>.
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
			if (!reading.Examine && WorldPlaceData.Hidden(WorldPlaceData.Parse(place?.Data))) (type, place) = (null, null);
			var terrain = type is null ? null : await reading.Legend.TerrainAsync(type);
			return field switch
			{
				"type" => new CallState(type ?? ""),
				"label" => new CallState(terrain?.Label ?? ""),
				"name" => new CallState(place?.Name ?? ""),
				"data" => new CallState(WorldPlaceData.Visible(place?.Data, reading.Examine) ?? ""),
				"closed" => new CallState(terrain?.Closed is not null),
				"reason" => new CallState(terrain?.Closed ?? ""),
				"id" => new CallState(type is null ? "" : WorldPlaceId.Of(dbref, at.X, at.Y, at.Z)),
				_ => new CallState(BadField),
			};
		});

	/// <summary>
	/// worldway(&lt;map&gt;, &lt;x&gt;, &lt;y&gt;, &lt;z&gt;, &lt;direction&gt;[, &lt;field&gt;]): the way out of a place toward a
	/// side or a diagonal: its <c>state</c> (the default: open, oneway, lock, closed, wall or none), <c>reason</c>, or
	/// <c>to</c>, the place it leads to.
	/// </summary>
	[SharpFunction(Name = "worldway", MinArgs = 5, MaxArgs = 6, Flags = FunctionFlags.Regular,
		ParameterNames = ["map", "x", "y", "z", "direction", "field"])]
	public static ValueTask<CallState> WorldWay(IMUSHCodeParser parser, SharpFunctionAttribute _2) =>
		ReadAsync(parser, async reading =>
		{
			if (Position(parser) is not { } at) return new CallState(BadPlace);
			if (WorldWays.ParseDirection(Arg(parser, 4)) is not { } direction) return new CallState(BadDirection);
			var field = Arg(parser, 5) is { Length: > 0 } given ? given.ToLowerInvariant() : "state";
			if (field is not ("state" or "reason" or "to")) return new CallState(BadField);
			var way = await WorldWays.WayAsync(reading.Store, reading.Record, reading.Legend, reading.Map.Object().DBRef, reading.Examine, at,
				direction);
			return field switch
			{
				"state" => new CallState(way.StateWord),
				"reason" => new CallState(way.Reason ?? ""),
				_ => new CallState(way.To.ToString()),
			};
		});

	/// <summary>
	/// worlddraw(&lt;map&gt;, &lt;x&gt;, &lt;y&gt;[, &lt;z&gt;[, &lt;radius&gt;[, &lt;mark&gt;]]]): the places within
	/// &lt;radius&gt; (3 by default, at most 20) of a place, one character each and north up, a row a line. A place
	/// with nothing painted is a space; &lt;mark&gt;, when given, stands in the centre.
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

	private static async ValueTask<string> GlyphAsync(Reading reading, WorldWindowRead? window, int x, int y)
	{
		if (window is null || !window.Cells.TryGetValue((x, y), out var cell) || reading.Record.Type(cell.Terrain) is not { } type) return " ";
		if (!reading.Examine && WorldPlaceData.Hidden(WorldPlaceData.Parse(window.Places.GetValueOrDefault((x, y))?.Data))) return " ";
		return (await reading.Legend.TerrainAsync(type)).Glyph;
	}
}
