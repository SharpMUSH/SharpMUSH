using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Plugins.World.Commands;

/// <summary>
/// <c>@world[/&lt;switch&gt;] &lt;map&gt;[/&lt;x&gt;,&lt;y&gt;[,&lt;z&gt;][/&lt;side&gt;]][=&lt;value&gt;]</c>: makes an object a stored
/// map and paints, walls, names and fills in its places, and places rooms on them. Every change needs control of the
/// map; reading it, examine. A locked side opens for whoever passes its key, as an exit's <c>@lock</c> does.
/// The left side is evaluated, and the right side too except for <c>/data</c>, whose JSON is taken as typed.
/// </summary>
public static partial class WorldCommand
{
	/// <summary>The most characters a terrain type has.</summary>
	public const int MaxTypeLength = 32;

	[GeneratedRegex("^[a-z0-9_-]+$")]
	private static partial Regex TypePattern();

	[SharpCommand(Name = "@WORLD", Switches = ["CREATE", "PAINT", "WALL", "LOCK", "PLACE", "DATA", "MERGE", "CLEAR", "ROOM", "SWEEP"],
		Behavior = CB.Default | CB.EqSplit | CB.NoParse, MinArgs = 0, MaxArgs = 2, ParameterNames = ["target", "value"])]
	public static async ValueTask<Option<CallState>> World(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var notify = parser.ServiceProvider.GetRequiredService<INotifyService>();
		var executor = await WorldMaps.ExecutorAsync(parser);
		var switches = parser.CurrentState.Switches.ToArray();
		var args = parser.CurrentState.Arguments;
		var left = args.TryGetValue("0", out var a0) ? (await a0.ParsedMessage())?.ToPlainText().Trim() ?? "" : "";
		var hasValue = args.TryGetValue("1", out var a1);
		var raw = hasValue ? a1!.Message?.ToPlainText() ?? "" : null;
		var value = hasValue && !switches.Contains("DATA") ? (await a1!.ParsedMessage())?.ToPlainText() ?? "" : raw;

		async ValueTask<Option<CallState>> Say(string text, string result)
		{
			await notify.Notify(executor, text);
			return new CallState(result);
		}

		if (switches.Contains("SWEEP"))
		{
			if (!await executor.IsWizard())
				return await Say(ErrorMessages.Notifications.PermissionDenied, ErrorMessages.Returns.PermissionDenied);
			var (returned, destroyed) = await parser.ServiceProvider.GetRequiredService<WorldRooms>().SweepAsync(parser, TimeSpan.Zero);
			return await Say($"Took back {Rooms(returned)}, and destroyed {destroyed} spare{(destroyed == 1 ? "" : "s")}.", returned.ToString());
		}

		var parts = left.Split('/', 3, StringSplitOptions.TrimEntries);
		if (parts[0].Length == 0) return await Say("World what?", ErrorMessages.Returns.NoMatch);
		if (await WorldMaps.LocateAsync(parser, executor, parts[0]) is not { } map) return new CallState(ErrorMessages.Returns.NoMatch);

		var permissions = parser.ServiceProvider.GetRequiredService<IPermissionService>();
		var reading = switches.Length == 0;
		if (!(reading ? await permissions.CanExamine(executor, map) : await permissions.Controls(executor, map)))
			return await Say(ErrorMessages.Notifications.PermissionDenied, ErrorMessages.Returns.PermissionDenied);

		var store = parser.ServiceProvider.GetRequiredService<WorldStore>();
		var attributes = parser.ServiceProvider.GetRequiredService<IAttributeService>();
		var dbref = map.Object().DBRef;
		var name = map.Object().Name;

		if (switches.Contains("CREATE")) return await CreateAsync();
		if (store.Map(dbref) is not { } record) return await Say($"{name} is not a world map.", "#-1 NOT A WORLD MAP");

		if (switches.Contains("PAINT")) return await PaintAsync();
		if (parts.Length < 2)
		{
			return reading
				? await Say(Describe(record), "")
				: await Say("Which place? Give it as <map>/<x>,<y>[,<z>].", "#-1 NO PLACE");
		}
		if (WorldPosition.Parse(parts[1]) is not { } at)
			return await Say($"{parts[1]} is not a place: give it as <x>,<y> or <x>,<y>,<z>.", "#-1 BAD PLACE");

		if (switches.Contains("WALL")) return await WallAsync(at);
		if (switches.Contains("LOCK")) return await LockAsync(at);
		if (switches.Contains("PLACE")) return await NameAsync(at);
		if (switches.Contains("DATA")) return await DataAsync(at);
		if (switches.Contains("ROOM")) return await RoomAsync(at);
		return reading ? await Say(await DescribePlaceAsync(at), "") : await Say("Use /data with /merge or /clear.", "#-1 BAD SWITCH");

		async ValueTask<Option<CallState>> CreateAsync()
		{
			var space = string.IsNullOrWhiteSpace(value) ? MapSpaces.Square : value.Trim().ToLowerInvariant();
			if (space != MapSpaces.Square)
				return await Say($"{space} maps are not stored yet; square is.", "#-1 BAD SPACE");
			if (!await store.CreateAsync(dbref, space)) return await Say($"{name} is already a world map.", "#-1 ALREADY A WORLD MAP");

			// The World map lists an object that names its kind; a stored map is drawn on a grid.
			if (await attributes.GetAttributeAsync(map, map, WorldLegend.KindAttribute, IAttributeService.AttributeMode.Read, parent: false)
				is not SharpAttribute[] { Length: > 0 })
			{
				await attributes.SetAttributeAsync(executor, map, WorldLegend.KindAttribute, MarkupText.Plain(MapKinds.Coordinates));
			}
			return await Say($"{name} is now a world map.", dbref.ToString());
		}

		async ValueTask<Option<CallState>> PaintAsync()
		{
			var range = parts.Length > 1 ? parts[1].Split(':', 2, StringSplitOptions.TrimEntries) : [];
			if (range.Length == 0 || WorldPosition.Parse(range[0]) is not { } from)
				return await Say("Which places? Give them as <map>/<x>,<y>[,<z>][:<x2>,<y2>].", "#-1 NO PLACE");
			var to = from;
			if (range.Length == 2)
			{
				if (WorldPosition.Parse($"{range[1]},{from.Z}") is not { } corner)
					return await Say($"{range[1]} is not a corner: give it as <x2>,<y2>.", "#-1 BAD PLACE");
				to = corner;
			}
			var covers = (Math.Abs((long)to.X - from.X) + 1) * (Math.Abs((long)to.Y - from.Y) + 1);
			if (covers > WorldStore.MaxPaint)
				return await Say($"That is {covers} places, more than {WorldStore.MaxPaint} at once; paint it in parts.", "#-1 TOO MANY PLACES");

			var type = string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
			if (type is not null && (type.Length > MaxTypeLength || !TypePattern().IsMatch(type)))
				return await Say($"A terrain type is letters, digits, _ and -, at most {MaxTypeLength} of them.", "#-1 BAD TYPE");

			if (await store.PaintAsync(dbref, from.Z, from.X, from.Y, to.X, to.Y, type) is not { } painted)
				return await Say($"{name} has as many terrain types as it can hold.", "#-1 TOO MANY TYPES");
			return await Say(type is null ? $"Erased {Places(painted)}." : $"Painted {Places(painted)} as {type}.", painted.ToString());
		}

		async ValueTask<Option<CallState>> WallAsync(WorldPosition place)
		{
			if (parts.Length < 3 || WorldGrid.ParseSide(parts[2]) is not { } side)
				return await Say("Which side? Give it as <map>/<x>,<y>[,<z>]/<n|e|s|w|u|d>.", "#-1 NO SIDE");
			var setting = (value ?? "").Split(',', 2, StringSplitOptions.TrimEntries);
			WorldEdge? edge = setting[0].ToLowerInvariant() switch
			{
				"" or "open" => WorldEdge.Open,
				"wall" => WorldEdge.Wall,
				"oneway" => WorldEdge.OneWayOut,
				"lock" => WorldEdge.Lock,
				_ => null,
			};
			if (edge is not { } set) return await Say("A side is open, wall, oneway or lock.", "#-1 BAD EDGE");
			var reason = setting.Length > 1 && setting[1].Length > 0 ? setting[1] : null;
			await store.SetEdgeAsync(dbref, place.X, place.Y, place.Z, side, set, reason);
			var what = set switch
			{
				WorldEdge.Open => "open",
				WorldEdge.Wall => "a wall",
				WorldEdge.OneWayOut => "one way, out",
				_ => "locked",
			};
			return await Say($"The {WorldGrid.Word(side)} side of {place} is now {what}.", "");
		}

		async ValueTask<Option<CallState>> LockAsync(WorldPosition place)
		{
			if (parts.Length < 3 || WorldGrid.ParseSide(parts[2]) is not { } side)
				return await Say("Which side? Give it as <map>/<x>,<y>[,<z>]/<n|e|s|w|u|d>.", "#-1 NO SIDE");
			if (string.IsNullOrWhiteSpace(value))
			{
				await store.SetLockAsync(dbref, place.X, place.Y, place.Z, side, null);
				return await Say($"The {WorldGrid.Word(side)} side of {place} is now open.", "");
			}
			var keys = parser.ServiceProvider.GetRequiredService<IBooleanExpressionParser>();
			var bound = await keys.BindAsync(value.Trim(), executor);
			if (bound is not string key)
				return await Say($"I don't understand that key: {(bound is Error<string> error ? error.Value : "")}", ErrorMessages.Returns.InvalidLock);
			await store.SetLockAsync(dbref, place.X, place.Y, place.Z, side, await StampAsync(key));
			return await Say($"The {WorldGrid.Word(side)} side of {place} is now locked.", "");
		}

		// Each object the key names is kept as its objid, as @lock keeps it, so the key fails once the object is
		// destroyed rather than passing to whatever takes its number next.
		async ValueTask<string> StampAsync(string key)
		{
			var numbers = new List<int>();
			LockKeyReferences.Rewrite(key, number =>
			{
				numbers.Add(number);
				return null;
			});
			var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
			var objids = new Dictionary<int, string>();
			foreach (var number in numbers.Distinct())
			{
				if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number))) is AnySharpObject found)
					objids[number] = found.Object().DBRef.ToString();
			}
			return LockKeyReferences.Rewrite(key, number => objids.GetValueOrDefault(number));
		}

		async ValueTask<Option<CallState>> NameAsync(WorldPosition place)
		{
			var placeName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
			await store.ChangePlaceAsync(dbref, place.X, place.Y, place.Z, current => current with { Name = placeName });
			return await Say(placeName is null ? $"Took the name off {place}." : $"Named {place} {placeName}.", "");
		}

		async ValueTask<Option<CallState>> DataAsync(WorldPosition place)
		{
			if (switches.Contains("CLEAR"))
			{
				await store.ChangePlaceAsync(dbref, place.X, place.Y, place.Z, current => current with { Data = null });
				return await Say($"Cleared the data of {place}.", "");
			}
			if (WorldPlaceData.Parse(raw) is not { } given)
				return await Say("The data must be a JSON object.", "#-1 NOT A JSON OBJECT");

			var limit = (int)parser.ServiceProvider.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Limit.MaxAttributeValueLength;
			var merge = switches.Contains("MERGE");
			var tooLong = false;
			await store.ChangePlaceAsync(dbref, place.X, place.Y, place.Z, current =>
			{
				var data = (merge ? WorldPlaceData.Merge(WorldPlaceData.Parse(current.Data), given) : given).ToJsonString();
				tooLong = Encoding.UTF8.GetByteCount(data) > limit;
				return tooLong ? current : current with { Data = data == "{}" ? null : data };
			});
			return tooLong
				? await Say($"That data is longer than {limit} bytes.", "#-1 TOO LONG")
				: await Say($"Set the data of {place}.", "");
		}

		async ValueTask<Option<CallState>> RoomAsync(WorldPosition place)
		{
			var rooms = parser.ServiceProvider.GetRequiredService<WorldRooms>();
			var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
			if (string.IsNullOrWhiteSpace(value))
			{
				if (await rooms.StandingAsync(dbref, place) is not { } standing)
					return await Say($"No room stands on {place}.", "#-1 NO ROOM");
				if (store.Room(standing) is not { Permanent: true })
					return await Say($"#{standing.Number} is lent to {place}, and goes back by itself once empty.", "#-1 LENT");
				await store.UnstandAsync(standing, release: false);
				await rooms.UnmarkAsync(standing);
				return await Say($"Took #{standing.Number} off {place}.", "");
			}

			if (await WorldMaps.LocateAsync(parser, executor, value.Trim()) is not { } found) return new CallState(ErrorMessages.Returns.NoMatch);
			if (!found.IsRoom) return await Say($"{found.Object().Name} is not a room.", "#-1 NOT A ROOM");
			if (!await permissions.Controls(executor, found))
				return await Say(ErrorMessages.Notifications.PermissionDenied, ErrorMessages.Returns.PermissionDenied);
			var room = found.Object().DBRef;
			if (await rooms.StandingAsync(dbref, place) is { } lent && lent.Number != room.Number
				&& store.Room(lent) is { Permanent: false } && await rooms.HoldsAnythingAsync(lent))
				return await Say($"#{lent.Number} is lent to {place} and holds something; place a room there once it is empty.", "#-1 OCCUPIED");

			var placing = await store.PlaceRoomAsync(dbref, place.X, place.Y, place.Z, room);
			if (placing == WorldPlacing.Taken)
				return await Say($"Another room stands on {place} to stay.", "#-1 TAKEN");
			if (placing == WorldPlacing.AlreadyThere)
				return await Say($"{found.Object().Name} already stands on {place}.", $"#{room.Number}");
			await mediator.Send(new SetObjectZoneCommand(found, map));
			await rooms.MarkAsync(found, await found.Object().Owner.WithCancellation(CancellationToken.None), map, place);
			return await Say(placing == WorldPlacing.Kept
				? $"{found.Object().Name} stays on {place} now."
				: $"{found.Object().Name} now stands on {place} to stay.", $"#{room.Number}");
		}

		async ValueTask<string> DescribePlaceAsync(WorldPosition place)
		{
			var legend = new WorldLegend(attributes, map);
			var cell = store.Cell(dbref, place.X, place.Y, place.Z);
			var held = store.Place(dbref, place.X, place.Y, place.Z);
			var lines = new List<string>();
			var type = record.Type(cell.Terrain);
			var terrain = type is null ? null : await legend.TerrainAsync(type);
			lines.Add($"{place} on {name}: {(terrain is null ? "nothing painted" : $"{terrain.Label} ({terrain.Type})")}"
				+ (held?.Name is { } placeName ? $", named {placeName}" : "") + ".");
			if (terrain?.Closed is { } closed) lines.Add($"Closed: {closed}");
			foreach (var side in Enum.GetValues<WorldSide>())
			{
				var (edge, reason, key) = store.Edge(dbref, place.X, place.Y, place.Z, side);
				if (edge == WorldEdge.Open) continue;
				var what = edge switch
				{
					WorldEdge.Wall => "wall",
					WorldEdge.OneWayOut => "one way, out",
					WorldEdge.OneWayIn => "one way, in",
					_ => "lock",
				};
				var shown = edge != WorldEdge.Lock ? ""
					: key is null ? ", no key"
					: $", key {await parser.ServiceProvider.GetRequiredService<IBooleanExpressionParser>().ReadbackAsync(key, executor)}";
				lines.Add($"{char.ToUpperInvariant(WorldGrid.Word(side)[0])}{WorldGrid.Word(side)[1..]}: {what}{shown}{(reason is null ? "" : $" ({reason})")}");
			}
			if (held?.Data is { } data) lines.Add($"Data: {data}");
			if (await parser.ServiceProvider.GetRequiredService<WorldRooms>().StandingAsync(dbref, place) is { } standing)
				lines.Add($"Room: #{standing.Number}{(store.Room(standing) is { Permanent: true } ? ", to stay" : ", lent")}");
			return string.Join("\n", lines);
		}

		string Describe(WorldMapRecord stored) =>
			$"{name} is a {stored.Space} world map with {stored.Palette.Count} terrain type{(stored.Palette.Count == 1 ? "" : "s")}"
			+ (stored.Painted
				? $", painted from {stored.Left},{stored.Bottom} to {stored.Right},{stored.Top} on level{(stored.Levels.Count == 1 ? "" : "s")} {string.Join(", ", stored.Levels)}."
				: ", nothing painted yet.");
	}

	private static string Places(long count) => count == 1 ? "1 place" : $"{count} places";

	private static string Rooms(int count) => count == 1 ? "1 empty room" : $"{count} empty rooms";
}
