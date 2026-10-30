using System.Globalization;
using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// The world-map package's maps, read for one viewer. A map is an object holding its own <c>MAP`KIND</c>; its rooms
/// are the rooms zoned to it, and a map zoned to another map is a region inside that one. Setting <c>MAP`KIND</c> publishes the map, so a viewer who may not examine its rooms
/// still sees the ones they can perceive and that are not DARK. What the map says about its rooms (their coordinates,
/// where a variable exit goes) is read as the map's owner, the builder who published it, never as the viewer.
/// A map whose places a map source (<see cref="IWorldMapSource"/>, from a plugin) stores is read the same way, and its
/// places a window at a time from that source.
/// </summary>
public sealed class WorldMapReader(IMediator mediator, IPermissionService permissions, IRealityPolicy reality,
	IAttributeService attributes, IConnectionService connections, IEnumerable<IWorldMapSource>? sources = null)
{
	public const string KindAttribute = "MAP`KIND";
	public const string WidthAttribute = "MAP`SIZE`W";
	public const string HeightAttribute = "MAP`SIZE`H";
	public const string LevelAttribute = "MAP`LEVEL";
	public const string BackdropAttribute = "MAP`BACKDROP";
	/// <summary>The place a room stands on, as the World plugin records it: <c>#map:created/x,y,z</c>.</summary>
	public const string PlaceAttribute = "MAP`PLACE";
	public const string LegendAttribute = "MAP`LEGEND";

	/// <summary>The kind of place a room is: the attribute gmcp-room sends a MUD client's mapper by default.</summary>
	public const string EnvironmentAttribute = "ENVIRONMENT";
	public const int MaxMaps = 500;
	public const int MaxRooms = 2000;
	public const int MaxExits = 8000;

	/// <summary>How many connected players one map read looks at for who is where.</summary>
	public const int MaxPeople = 1000;

	/// <summary>How many kinds of place one map's legend names.</summary>
	public const int MaxLegend = 100;

	/// <summary>How many rooms a route search reaches before it gives up.</summary>
	public const int MaxRouteRooms = 5000;

	/// <summary>How many rooms on no map a builder is offered to add.</summary>
	public const int MaxNearby = 50;

	/// <summary>How many zones up from the viewer's room a region is looked for.</summary>
	public const int MaxDepth = 32;

	/// <summary>A room's cell on a coordinate or compass map.</summary>
	public static readonly IReadOnlyList<string> Coordinates = ["MAP`COORDS`X", "MAP`COORDS`Y", "MAP`COORDS`Z"];

	/// <summary>A room's point on a graph map's image.</summary>
	public static readonly IReadOnlyList<string> ImagePosition = ["MAP`POS`X", "MAP`POS`Y"];

	private static readonly IReadOnlyList<string> Size = [WidthAttribute, HeightAttribute];

	private const int IndexPage = 200;

	/// <summary>Every map <paramref name="viewer"/> may see, by name. Empty while the world-map package is not installed.</summary>
	public async Task<IReadOnlyList<MapSummaryDto>> ListAsync(AnySharpObject viewer, CancellationToken ct)
	{
		var maps = new List<AnySharpObject>();
		IndexedAttributeCursor? after = null;
		do
		{
			var spec = new IndexedAttributeQuerySpec(KindAttribute, IndexedAttributeOperation.First, string.Empty, null, after,
				IndexPage, Facts: false);
			if (await mediator.Send(new GetIndexedAttributeQuery(spec), ct) is not IndexedAttributePage page) break;
			foreach (var hit in page.Hits)
			{
				if (maps.Count == MaxMaps) break;
				if (await MapAsync(viewer, hit.Number, ct) is { } map
					&& maps.All(known => known.Object().DBRef.Number != hit.Number)) maps.Add(map);
			}
			after = maps.Count < MaxMaps ? page.Next : null;
		} while (after is not null);

		var summaries = new List<MapSummaryDto>(maps.Count);
		foreach (var map in maps) summaries.Add(await SummaryAsync(viewer, map, ct));
		return [.. summaries.OrderBy(map => map.Name, StringComparer.OrdinalIgnoreCase).ThenBy(map => Number(map.Id))];
	}

	/// <summary>The map <paramref name="number"/>, with the rooms and exits <paramref name="viewer"/> may see; NotFound when it is no map they may see.</summary>
	public async Task<Found<MapDto>> ReadAsync(AnySharpObject viewer, int number, CancellationToken ct)
	{
		if (await MapAsync(viewer, number, ct) is not { } map) return new NotFound();
		var publisher = new AnySharpObject(await map.Object().Owner.WithCancellation(ct));
		var summary = await SummaryAsync(viewer, map, ct);
		var position = summary.Kind == MapKinds.Graph ? ImagePosition : Coordinates;
		var truncated = false;
		// Only someone who controls the map may plan changes to it; they may change only the rooms they control.
		var canEdit = await permissions.Controls(viewer, map);
		var (source, space) = await SourceAsync(map, ct) is var (found, stored) ? (found, stored) : ((IWorldMapSource?)null, (MapSpaceDto?)null);

		var rooms = new List<(AnySharpObject Room, MapRoomDto Dto)>();
		var regions = new List<MapRoomDto>();
		await foreach (var member in mediator.CreateStream(new GetObjectsByZoneQuery(map.Object().DBRef), ct))
		{
			if (!string.Equals(member.Type, "ROOM", StringComparison.OrdinalIgnoreCase))
			{
				if (regions.Count < MaxMaps && member.DBRef != map.Object().DBRef
					&& await MapAsync(viewer, member.DBRef.Number, ct) is { } region)
				{
					regions.Add(new($"#{member.DBRef.Number}", region.Object().Name, await NumbersAsync(region, region, position, ct),
						Positive(await NumbersAsync(region, region, Size, ct))));
				}
				continue;
			}
			if (rooms.Count == MaxRooms)
			{
				truncated = true;
				break;
			}
			if (await mediator.Send(new GetObjectNodeQuery(member.DBRef), ct) is not AnySharpObject room || !room.IsRoom) continue;
			if (!await VisibleAsync(viewer, room, ct)) continue;
			// A room's place is read as the room, as a region's is: a builder may dig a room into a map someone else owns.
			// A room standing on a place of a stored map has no coordinates of its own: it is drawn at its place, and left
			// off the map with it when the place is hidden from the viewer.
			var at = await NumbersAsync(room, room, position, ct);
			if (at is null && source is not null && PlacePosition(await OwnValueAsync(room, room, PlaceAttribute), map) is { } place)
			{
				if (await source.HiddenAsync(viewer, map, (int)place[0], (int)place[1], (int)place[2], ct)) continue;
				at = place;
			}
			rooms.Add((room, new($"#{room.Object().DBRef.Number}", room.Object().Name, at,
				Positive(await NumbersAsync(room, room, Size, ct)))
			{
				Editable = canEdit && await permissions.Controls(viewer, room),
				Environment = await OwnValueAsync(room, room, EnvironmentAttribute) is { Length: > 0 } environment
					? environment.ToLowerInvariant()
					: null,
			}));
		}

		var onMap = rooms.Select(entry => entry.Room.Object().DBRef.Number).ToHashSet();
		var people = await PeopleAsync(viewer, onMap, ct);
		rooms = [.. rooms.Select(entry => people.TryGetValue(entry.Room.Object().DBRef.Number, out var here)
			? (entry.Room, entry.Dto with { People = [.. here.Order(StringComparer.OrdinalIgnoreCase)] })
			: entry)];
		var exits = new List<MapExitDto>();
		var otherMaps = new Dictionary<int, MapSummaryDto?>();
		foreach (var (room, dto) in rooms.OrderBy(entry => entry.Room.Object().DBRef.Number))
		{
			if (truncated && exits.Count == MaxExits) break;
			await foreach (var exit in mediator.CreateStream(new GetExitsQuery(room.Object().DBRef), ct))
			{
				if (exits.Count == MaxExits)
				{
					truncated = true;
					break;
				}
				var target = new AnySharpObject(exit);
				if (!await VisibleAsync(viewer, target, ct)) continue;
				// An exit already destroyed waits only to be purged.
				if (await exit.Object.IsGoingAsync()) continue;
				// A stale list entry must not draw an exit moved out of the room.
				if ((await exit.Location.WithCancellation(ct)).Object().DBRef != room.Object().DBRef) continue;
				var (destination, destinationName, destinationMap) = await DestinationAsync(viewer, publisher, exit, map, onMap, otherMaps, ct);
				exits.Add(new($"#{exit.Object.DBRef.Number}", exit.Object.Name, [.. target.Aliases ?? []], dto.Id, destination,
					destinationMap)
				{
					DestinationName = destinationName,
					Editable = canEdit && await permissions.Controls(viewer, target),
				});
			}
		}

		var where = (await viewer.Where()).Object();
		var here = where.DBRef.Number;
		var nearby = canEdit ? await NearbyAsync(viewer, exits, onMap, ct) : [];
		return new MapDto(summary, Positive(await NumbersAsync(map, map, Size, ct)), [.. rooms.Select(entry => entry.Dto)], exits,
			onMap.Contains(here) ? $"#{here}" : null, truncated)
		{
			Regions = regions,
			CanEdit = canEdit,
			Nearby = nearby,
			HereRegion = await HereRegionAsync(where, map, regions, ct),
			Level = int.TryParse(await OwnValueAsync(map, map, LevelAttribute), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
				out var level) ? level : null,
			LevelNames = await LevelNamesAsync(map),
			Legend = await LegendAsync(map, ct),
			Backdrop = await OwnValueAsync(map, map, BackdropAttribute) is { Length: > 0 } backdrop ? backdrop : null,
			Space = space,
		};
	}

	/// <summary>
	/// The places of <paramref name="window"/> on map <paramref name="number"/>, from the map source that stores them, as
	/// <paramref name="viewer"/> may see them; NotFound when it is no map they may see, or no source stores its places.
	/// </summary>
	public async Task<Found<MapCellsDto>> CellsAsync(AnySharpObject viewer, int number, MapWindow window, CancellationToken ct)
	{
		if (await MapAsync(viewer, number, ct) is not { } map || await SourceAsync(map, ct) is not var (source, _))
			return new NotFound();
		return await source.CellsAsync(viewer, map, window, ct);
	}

	/// <summary>The first map source that stores <paramref name="map"/>'s places, and the space it stores them in.</summary>
	private async Task<(IWorldMapSource Source, MapSpaceDto Space)?> SourceAsync(AnySharpObject map, CancellationToken ct)
	{
		foreach (var source in sources ?? [])
		{
			if (await source.SpaceAsync(map, ct) is MapSpaceDto space) return (source, space);
		}
		return null;
	}

	/// <summary>
	/// The connected players in each room of <paramref name="rooms"/> that <paramref name="viewer"/> may know are there, by
	/// room: ones <c>loc()</c> would find for them (<c>Can_Locate</c>), that they can see, and that have not hidden from
	/// <c>WHO</c>, unless they may examine them. At most <see cref="MaxPeople"/> players are looked at.
	/// </summary>
	private async Task<Dictionary<int, List<string>>> PeopleAsync(AnySharpObject viewer, HashSet<int> rooms, CancellationToken ct)
	{
		var people = new Dictionary<int, List<string>>();
		var players = connections.GetAll()
			.Where(connection => connection.Ref is not null && connection.State == IConnectionService.ConnectionState.LoggedIn)
			.Select(connection => connection.Ref!.Value.Number)
			.Distinct()
			.Take(MaxPeople);
		await foreach (var number in players.WithCancellation(ct))
		{
			if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), ct) is not AnySharpObject { IsPlayer: true } player) continue;
			var room = (await player.Where()).Object().DBRef.Number;
			if (!rooms.Contains(room) || !await permissions.CanLocate(viewer, player) || !await permissions.CanSee(viewer, player)) continue;
			if (await connections.IsPlayerHiddenAsync(player.Object().DBRef) && !await permissions.CanExamine(viewer, player)) continue;
			if (!people.TryGetValue(room, out var here)) people[room] = here = [];
			here.Add(player.Object().Name);
		}
		return people;
	}

	/// <summary>
	/// The rooms this map's exits lead to that are on no map, and that <paramref name="viewer"/> controls: the rooms a
	/// builder may add to it. At most <see cref="MaxNearby"/>, lowest-numbered first.
	/// </summary>
	private async Task<List<MapRoomDto>> NearbyAsync(AnySharpObject viewer, List<MapExitDto> exits, HashSet<int> onMap,
		CancellationToken ct)
	{
		var nearby = new List<MapRoomDto>();
		var candidates = exits
			.Where(exit => exit.Destination is not null && exit.DestinationMap is null)
			.Select(exit => Number(exit.Destination!))
			.Where(number => !onMap.Contains(number))
			.Distinct()
			.Order();
		foreach (var number in candidates)
		{
			if (nearby.Count == MaxNearby) break;
			if (await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), ct) is not AnySharpObject room || !room.IsRoom) continue;
			// A room zoned to a map is on that map already, whether or not the viewer may see the map.
			if (await room.Object().Zone.WithCancellation(ct) is AnySharpObject zone
				&& MapKinds.Parse(await OwnValueAsync(zone, zone, KindAttribute)) is not null) continue;
			if (!await permissions.Controls(viewer, room)) continue;
			nearby.Add(new($"#{number}", room.Object().Name, null, null) { Editable = true });
		}
		return nearby;
	}

	/// <summary>Where <paramref name="viewer"/> stands, and the map that is on when it is one they may see.</summary>
	public async Task<MapHereDto> HereAsync(AnySharpObject viewer, CancellationToken ct)
	{
		var where = (await viewer.Where()).Object();
		var map = await where.Zone.WithCancellation(ct) is AnySharpObject zone && await MapAsync(viewer, zone.Object().DBRef.Number, ct) is { } found
			? await SummaryAsync(viewer, found, ct)
			: null;
		return new($"#{where.DBRef.Number}", where.Name, map);
	}

	/// <summary>
	/// The fewest exits from where <paramref name="viewer"/> stands to room <paramref name="number"/>, over exits and rooms
	/// they may see, on any map or none; among equal routes, the one through lower-numbered exits. Locks are not
	/// tried: <c>walk</c> stops at one that refuses. NotFound when the room is not one they may see, or no route
	/// within <see cref="MaxRouteRooms"/> rooms reaches it.
	/// </summary>
	public async Task<Found<MapRouteDto>> RouteAsync(AnySharpObject viewer, int number, CancellationToken ct)
	{
		if (await RoomAsync(viewer, number, ct) is not AnySharpObject) return new NotFound();
		var start = (await viewer.Where()).Object().DBRef;
		if (start.Number == number) return new MapRouteDto([], null);

		var came = new Dictionary<int, (SharpExit Exit, AnySharpObject Room, int From)>();
		var queue = new Queue<DBRef>([start]);
		while (queue.Count > 0 && came.Count < MaxRouteRooms)
		{
			var at = queue.Dequeue();
			var exits = mediator.CreateStream(new GetExitsQuery(at), ct)
				.OrderBy(exit => exit.Object.DBRef.Number)
				.Where(async (exit, token) => await VisibleAsync(viewer, new AnySharpObject(exit), token)
					// A stale list entry must not route through an exit moved out of the room.
					&& (await exit.Location.WithCancellation(token)).Object().DBRef.Number == at.Number);
			await foreach (var exit in exits.WithCancellation(ct))
			{
				var reader = new AnySharpObject(await exit.Object.Owner.WithCancellation(ct));
				if (await LinkedRoomAsync(reader, exit, ct) is not { } room || !await VisibleAsync(viewer, room, ct)) continue;
				var next = room.Object().DBRef;
				if (next.Number == start.Number || !came.TryAdd(next.Number, (exit, room, at.Number))) continue;
				if (next.Number == number) return await StepsAsync(viewer, came, start.Number, number, ct);
				queue.Enqueue(next);
			}
		}
		return new NotFound();
	}

	/// <summary>Room <paramref name="number"/>, when it is a room <paramref name="viewer"/> may see.</summary>
	public async Task<Found<AnySharpObject>> RoomAsync(AnySharpObject viewer, int number, CancellationToken ct) =>
		number >= 0 && await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), ct) is AnySharpObject { IsRoom: true } room
			&& await VisibleAsync(viewer, room, ct)
			? room
			: new NotFound();

	private async Task<MapRouteDto> StepsAsync(AnySharpObject viewer,
		Dictionary<int, (SharpExit Exit, AnySharpObject Room, int From)> came, int start, int end, CancellationToken ct)
	{
		var path = new List<(SharpExit Exit, AnySharpObject Room, int From)>();
		for (var at = end; at != start; at = came[at].From) path.Insert(0, came[at]);

		var maps = new Dictionary<int, MapSummaryDto?>();
		var steps = new List<MapStepDto>(path.Count);
		var words = new List<string?>(path.Count);
		foreach (var (exit, room, from) in path)
		{
			MapSummaryDto? map = null;
			if (await room.Object().Zone.WithCancellation(ct) is AnySharpObject zone)
			{
				var zoneNumber = zone.Object().DBRef.Number;
				if (!maps.TryGetValue(zoneNumber, out map))
				{
					map = await MapAsync(viewer, zoneNumber, ct) is { } found ? await SummaryAsync(viewer, found, ct) : null;
					maps[zoneNumber] = map;
				}
			}
			steps.Add(new($"#{exit.Object.DBRef.Number}", exit.Object.Name, $"#{from}", $"#{room.Object().DBRef.Number}",
				room.Object().Name, map));
			words.Add(Word(exit.Object.Name, new AnySharpObject(exit).Aliases ?? []));
		}
		return new MapRouteDto(steps, words.Contains(null) ? null : "walk " + string.Join(' ', words));
	}

	/// <summary>
	/// What to type to take an exit in a <c>walk</c> line: its name, or its first alias with no space or double quote,
	/// or else its name in double quotes; null when it has no name <c>walk</c> can take.
	/// </summary>
	public static string? Word(string name, IEnumerable<string> aliases) =>
		new[] { name }.Concat(aliases)
			.Select(word => word.Trim())
			.FirstOrDefault(word => word.Length > 0 && !word.Any(letter => char.IsWhiteSpace(letter) || letter == '"'))
		?? (name.Trim() is { Length: > 0 } spaced && !spaced.Contains('"') ? $"\"{spaced}\"" : null);

	/// <summary>
	/// The region of <paramref name="regions"/> that <paramref name="where"/> is inside: up its zone, and that zone's zone,
	/// until one is zoned to <paramref name="map"/>. Null when its own zone is the map, or the chain never reaches it.
	/// </summary>
	private static async Task<string?> HereRegionAsync(SharpObject where, AnySharpObject map, List<MapRoomDto> regions,
		CancellationToken ct)
	{
		string? inside = null;
		var at = where;
		for (var step = 0; step < MaxDepth && await at.Zone.WithCancellation(ct) is AnySharpObject zone; step++)
		{
			if (zone.Object().DBRef == map.Object().DBRef) return regions.Any(region => region.Id == inside) ? inside : null;
			(inside, at) = ($"#{zone.Object().DBRef.Number}", zone.Object());
		}
		return null;
	}

	/// <summary>The names <paramref name="map"/> gives its levels, one <c>MAP`LEVEL`&lt;level&gt;</c> each; a blank one names nothing.</summary>
	private async Task<IReadOnlyDictionary<int, string>> LevelNamesAsync(AnySharpObject map)
	{
		var names = new Dictionary<int, string>();
		if (await attributes.GetAttributePatternAsync(map, map, $"{LevelAttribute}`*", checkParents: false,
				IAttributeService.AttributePatternMode.Wildcard) is not SharpAttribute[] found) return names;
		foreach (var attribute in found)
		{
			var name = attribute.Value.ToPlainText().Trim();
			if (name.Length > 0 && int.TryParse(attribute.LongName.AsSpan(LevelAttribute.Length + 1), NumberStyles.AllowLeadingSign,
					CultureInfo.InvariantCulture, out var level)) names[level] = name;
		}
		return names;
	}

	/// <summary>
	/// The legend <paramref name="map"/> draws its rooms by: each <c>MAP`LEGEND`&lt;type&gt;`TONE</c>, <c>`ICON</c> and
	/// <c>`LABEL</c> on it, then on the map it is zoned to, and so up; a type takes all three from the nearest map that
	/// names it. Each map's legend is read as that map.
	/// </summary>
	private async Task<IReadOnlyList<MapLegendDto>> LegendAsync(AnySharpObject map, CancellationToken ct)
	{
		var legend = new Dictionary<string, MapLegendDto>(StringComparer.Ordinal);
		var seen = new HashSet<int>();
		for (AnySharpObject? at = map; at is not null && legend.Count < MaxLegend && seen.Count < MaxDepth
			&& seen.Add(at.Object().DBRef.Number); at = await ParentMapAsync(at, ct))
		{
			if (await attributes.GetAttributePatternAsync(at, at, $"{LegendAttribute}`*`*", checkParents: false,
					IAttributeService.AttributePatternMode.Wildcard) is not SharpAttribute[] found) continue;
			var types = found
				.Select(attribute => (Name: attribute.LongName.Split('`'), Value: attribute.Value.ToPlainText().Trim()))
				.Where(entry => entry.Name.Length == 4 && entry.Value.Length > 0)
				.GroupBy(entry => entry.Name[2].ToLowerInvariant())
				.Where(type => !legend.ContainsKey(type.Key));
			foreach (var type in types)
			{
				string? Field(string field) => type.FirstOrDefault(entry => entry.Name[3].Equals(field, StringComparison.OrdinalIgnoreCase)).Value;
				legend[type.Key] = new(type.Key, Field("LABEL") ?? type.Key, Field("TONE")?.ToLowerInvariant(), Field("ICON"));
			}
		}
		return [.. legend.Values.OrderBy(entry => entry.Label, StringComparer.OrdinalIgnoreCase).Take(MaxLegend)];
	}

	/// <summary>The map <paramref name="map"/> is zoned to, when that holds a <c>MAP`KIND</c> of its own.</summary>
	private async Task<AnySharpObject?> ParentMapAsync(AnySharpObject map, CancellationToken ct)
		=> await map.Object().Zone.WithCancellation(ct) is AnySharpObject zone
			&& MapKinds.Parse(await OwnValueAsync(zone, zone, KindAttribute)) is not null
				? zone
				: null;

	/// <summary>Object <paramref name="number"/> when it holds its own <c>MAP`KIND</c> naming a kind, and <paramref name="viewer"/> may see it.</summary>
	private async Task<AnySharpObject?> MapAsync(AnySharpObject viewer, int number, CancellationToken ct)
	{
		if (number < 0 || await mediator.Send(new GetObjectNodeQuery(new DBRef(number)), ct) is not AnySharpObject map) return null;
		return MapKinds.Parse(await OwnValueAsync(map, map, KindAttribute)) is not null && await VisibleAsync(viewer, map, ct)
			? map
			: null;
	}

	private async Task<MapSummaryDto> SummaryAsync(AnySharpObject viewer, AnySharpObject map, CancellationToken ct)
	{
		var kind = MapKinds.Parse(await OwnValueAsync(map, map, KindAttribute)) ?? MapKinds.Graph;
		var parent = await map.Object().Zone.WithCancellation(ct) is AnySharpObject zone
			&& zone.Object().DBRef.Number is var number && number != map.Object().DBRef.Number
			&& await MapAsync(viewer, number, ct) is not null
				? $"#{number}"
				: null;
		return new($"#{map.Object().DBRef.Number}", map.Object().Name, kind, parent);
	}

	/// <summary>
	/// Where <paramref name="exit"/> leads, as far as <paramref name="viewer"/> may know: a room they can see, and the
	/// map it is on when that is another one. A variable or HOME link, or one the publisher cannot read, leads nowhere
	/// on the map, and the destination relation is never resolved for it.
	/// </summary>
	private async Task<(string? Room, string? Name, MapSummaryDto? Map)> DestinationAsync(AnySharpObject viewer,
		AnySharpObject publisher, SharpExit exit, AnySharpObject map, HashSet<int> onMap, Dictionary<int, MapSummaryDto?> otherMaps,
		CancellationToken ct)
	{
		if (await LinkedRoomAsync(publisher, exit, ct) is not { } room) return (null, null, null);
		var number = room.Object().DBRef.Number;
		var name = room.Object().Name;
		if (onMap.Contains(number)) return ($"#{number}", name, null);
		if (!await VisibleAsync(viewer, room, ct)) return (null, null, null);
		if (await room.Object().Zone.WithCancellation(ct) is not AnySharpObject zone
			|| zone.Object().DBRef.Number == map.Object().DBRef.Number) return ($"#{number}", name, null);
		var zoneNumber = zone.Object().DBRef.Number;
		if (!otherMaps.TryGetValue(zoneNumber, out var other))
		{
			other = await MapAsync(viewer, zoneNumber, ct) is { } found ? await SummaryAsync(viewer, found, ct) : null;
			otherMaps[zoneNumber] = other;
		}
		return ($"#{number}", name, other);
	}

	/// <summary>
	/// The room <paramref name="exit"/> is linked to, as <paramref name="reader"/> may know it; null for a variable or HOME
	/// link, one the reader cannot read, or a link to something other than a room. The destination relation is never
	/// resolved for a variable or HOME link.
	/// </summary>
	private async Task<AnySharpObject?> LinkedRoomAsync(AnySharpObject reader, SharpExit exit, CancellationToken ct)
	{
		var marker = await attributes.GetAttributeAsync(reader, new AnySharpObject(exit), "_LINKTYPE",
			IAttributeService.AttributeMode.Read, parent: false);
		if (marker is Error<string>) return null;
		if (marker is SharpAttribute[] { Length: > 0 } chain
			&& chain[^1].Value.ToPlainText().Trim().ToLowerInvariant() is "variable" or "home") return null;
		if (await exit.Home.WithCancellation(ct) is not AnySharpContainer destination) return null;
		var room = destination.WithExitOption();
		return room.IsRoom ? room : null;
	}

	/// <summary>Whether <paramref name="viewer"/> perceives <paramref name="target"/>, and it is not DARK or they may examine it.</summary>
	private async Task<bool> VisibleAsync(AnySharpObject viewer, AnySharpObject target, CancellationToken ct) =>
		await reality.CanPerceiveAsync(viewer.Object().DBRef, target.Object().DBRef, ct)
		&& (!await target.IsDark() || await permissions.CanExamine(viewer, target));

	/// <summary>The value of <paramref name="name"/> stored on <paramref name="target"/> itself, read as <paramref name="reader"/>; empty when unset or unreadable.</summary>
	private async Task<string> OwnValueAsync(AnySharpObject reader, AnySharpObject target, string name)
		=> await attributes.GetAttributeAsync(reader, target, name, IAttributeService.AttributeMode.Read, parent: false)
			is SharpAttribute[] { Length: > 0 } chain
			? chain[^1].Value.ToPlainText().Trim()
			: string.Empty;

	/// <summary>
	/// The numbers in <paramref name="names"/> on <paramref name="target"/>, as gmcp-room reads coordinates: a missing one
	/// is 0, and with none set, or one that is not a number, there are none.
	/// </summary>
	private async Task<double[]?> NumbersAsync(AnySharpObject reader, AnySharpObject target, IReadOnlyList<string> names,
		CancellationToken ct)
	{
		var values = new string[names.Count];
		for (var i = 0; i < values.Length; i++)
		{
			ct.ThrowIfCancellationRequested();
			values[i] = await OwnValueAsync(reader, target, names[i]);
		}
		if (values.All(string.IsNullOrEmpty)) return null;
		var axes = new double[values.Length];
		for (var i = 0; i < values.Length; i++)
		{
			if (values[i].Length == 0) continue;
			if (!double.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out axes[i]) || !double.IsFinite(axes[i])) return null;
		}
		return axes;
	}

	/// <summary>
	/// The position in a room's <see cref="PlaceAttribute"/>, <c>#map:created/x,y,z</c>, when the place is on
	/// <paramref name="map"/>; null for a place on another map or a value that is no place.
	/// </summary>
	private static double[]? PlacePosition(string place, AnySharpObject map)
	{
		var slash = place.IndexOf('/');
		if (slash < 1 || place[0] != '#') return null;
		var id = place[1..slash].Split(':');
		if (!int.TryParse(id[0], NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number != map.Object().DBRef.Number) return null;
		if (id.Length > 1 && (!long.TryParse(id[1], NumberStyles.None, CultureInfo.InvariantCulture, out var created)
			|| created != map.Object().CreationTime)) return null;
		var axes = place[(slash + 1)..].Split(',');
		if (axes.Length != 3) return null;
		var position = new double[3];
		for (var i = 0; i < 3; i++)
		{
			if (!int.TryParse(axes[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var axis)) return null;
			position[i] = axis;
		}
		return position;
	}

	/// <summary>A size: each side at least 1, a missing one as 1.</summary>
	private static double[]? Positive(double[]? size) => size?.Select(side => Math.Max(side, 1)).ToArray();

	private static int Number(string dbref) => int.Parse(dbref.AsSpan(1), CultureInfo.InvariantCulture);
}
