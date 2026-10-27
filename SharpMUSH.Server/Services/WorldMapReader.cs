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
/// </summary>
public sealed class WorldMapReader(IMediator mediator, IPermissionService permissions, IRealityPolicy reality,
	IAttributeService attributes)
{
	public const string KindAttribute = "MAP`KIND";
	public const string WidthAttribute = "MAP`SIZE`W";
	public const string HeightAttribute = "MAP`SIZE`H";
	public const int MaxMaps = 500;
	public const int MaxRooms = 2000;
	public const int MaxExits = 8000;

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

		var rooms = new List<(AnySharpObject Room, MapRoomDto Dto)>();
		await foreach (var member in mediator.CreateStream(new GetObjectsByZoneQuery(map.Object().DBRef), ct))
		{
			if (!string.Equals(member.Type, "ROOM", StringComparison.OrdinalIgnoreCase)) continue;
			if (rooms.Count == MaxRooms)
			{
				truncated = true;
				break;
			}
			if (await mediator.Send(new GetObjectNodeQuery(member.DBRef), ct) is not AnySharpObject room || !room.IsRoom) continue;
			if (!await VisibleAsync(viewer, room, ct)) continue;
			rooms.Add((room, new($"#{room.Object().DBRef.Number}", room.Object().Name, await NumbersAsync(publisher, room, position, ct),
				Positive(await NumbersAsync(publisher, room, Size, ct)))));
		}

		var onMap = rooms.Select(entry => entry.Room.Object().DBRef.Number).ToHashSet();
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
				// A stale list entry must not draw an exit moved out of the room.
				if ((await exit.Location.WithCancellation(ct)).Object().DBRef != room.Object().DBRef) continue;
				var (destination, destinationMap) = await DestinationAsync(viewer, publisher, exit, map, onMap, otherMaps, ct);
				exits.Add(new($"#{exit.Object.DBRef.Number}", exit.Object.Name, [.. target.Aliases ?? []], dto.Id, destination,
					destinationMap));
			}
		}

		var here = (await viewer.Where()).Object().DBRef.Number;
		return new MapDto(summary, Positive(await NumbersAsync(map, map, Size, ct)), [.. rooms.Select(entry => entry.Dto)], exits,
			onMap.Contains(here) ? $"#{here}" : null, truncated);
	}

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
	private async Task<(string? Room, MapSummaryDto? Map)> DestinationAsync(AnySharpObject viewer, AnySharpObject publisher,
		SharpExit exit, AnySharpObject map, HashSet<int> onMap, Dictionary<int, MapSummaryDto?> otherMaps, CancellationToken ct)
	{
		var marker = await attributes.GetAttributeAsync(publisher, new AnySharpObject(exit), "_LINKTYPE",
			IAttributeService.AttributeMode.Read, parent: false);
		if (marker is Error<string>) return (null, null);
		if (marker is SharpAttribute[] { Length: > 0 } chain
			&& chain[^1].Value.ToPlainText().Trim().ToLowerInvariant() is "variable" or "home") return (null, null);
		if (await exit.Home.WithCancellation(ct) is not AnySharpContainer destination) return (null, null);
		var room = destination.WithExitOption();
		if (!room.IsRoom) return (null, null);
		var number = room.Object().DBRef.Number;
		if (onMap.Contains(number)) return ($"#{number}", null);
		if (!await VisibleAsync(viewer, room, ct)) return (null, null);
		if (await room.Object().Zone.WithCancellation(ct) is not AnySharpObject zone
			|| zone.Object().DBRef.Number == map.Object().DBRef.Number) return ($"#{number}", null);
		var zoneNumber = zone.Object().DBRef.Number;
		if (!otherMaps.TryGetValue(zoneNumber, out var other))
		{
			other = await MapAsync(viewer, zoneNumber, ct) is { } found ? await SummaryAsync(viewer, found, ct) : null;
			otherMaps[zoneNumber] = other;
		}
		return ($"#{number}", other);
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

	/// <summary>A size: each side at least 1, a missing one as 1.</summary>
	private static double[]? Positive(double[]? size) => size?.Select(side => Math.Max(side, 1)).ToArray();

	private static int Number(string dbref) => int.Parse(dbref.AsSpan(1), CultureInfo.InvariantCulture);
}
