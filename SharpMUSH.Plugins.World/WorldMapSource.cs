using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World;

/// <summary>
/// The World map's view of the stored maps: the space a map's places are in, and a window of them as a viewer may see
/// them. Reads only. A place whose data hides it (<c>render.hidden</c>) is drawn as its bare terrain, with none of its
/// walls, for a viewer who may not examine the map.
/// </summary>
public sealed class WorldMapSource(WorldStore store, IAttributeService attributes, IPermissionService permissions) : IWorldMapSource
{
	public ValueTask<Found<MapSpaceDto>> SpaceAsync(AnySharpObject map, CancellationToken ct) =>
		ValueTask.FromResult<Found<MapSpaceDto>>(store.Map(map.Object().DBRef) is { } record
			? new MapSpaceDto(record.Space, record.Left, record.Bottom, record.Right, record.Top, record.Levels)
			: new NotFound());

	public async ValueTask<bool> HiddenAsync(AnySharpObject viewer, AnySharpObject map, int x, int y, int level, CancellationToken ct) =>
		store.Window(map.Object().DBRef, level, x, y, x, y) is { } read
		&& WorldPlaceData.Hidden(WorldPlaceData.Parse(read.Places.GetValueOrDefault((x, y))?.Data))
		&& !await permissions.CanExamine(viewer, map);

	public async ValueTask<MapCellsDto> CellsAsync(AnySharpObject viewer, AnySharpObject map, MapWindow window, CancellationToken ct)
	{
		var (level, left, bottom, right, top) = (window.Level, window.Left, window.Bottom, window.Right, window.Top);
		var empty = new MapCellsDto(level, left, bottom, right, top, [], [], false);
		if (window.Cells > MapWindow.MaxCells) return empty with { Truncated = true };
		var dbref = map.Object().DBRef;
		// The window reads a row more above it: a hex place's northwest edge is stored on the place up and to the left.
		if (store.Window(dbref, level, left, bottom, right, top + 1) is not { } read) return empty;
		var space = read.Map.Space;

		var examine = await permissions.CanExamine(viewer, map);
		var legend = new WorldLegend(attributes, map);
		bool Inside(int x, int y) => x >= left && x <= right && y >= bottom && y <= top;

		var cells = new List<MapCellDto>();
		foreach (var ((x, y), cell) in read.Cells.OrderByDescending(entry => entry.Key.Y).ThenBy(entry => entry.Key.X))
		{
			if (!Inside(x, y) || read.Map.Type(cell.Terrain) is not { } type) continue;
			var place = read.Places.GetValueOrDefault((x, y));
			var data = WorldPlaceData.Parse(place?.Data);
			// A hidden place reads as bare terrain: nothing of the place itself shows.
			if (!examine && WorldPlaceData.Hidden(data)) (place, data) = (null, null);
			var terrain = await legend.TerrainAsync(type);
			cells.Add(new MapCellDto(x, y, type)
			{
				Id = WorldPlaceId.Of(dbref, x, y, level),
				Name = place?.Name,
				Closed = terrain.Closed is not null,
				Reason = terrain.Closed,
				Icon = WorldPlaceData.Render(data, "icon"),
				Tone = WorldPlaceData.Render(data, "tone")?.ToLowerInvariant(),
				Label = WorldPlaceData.Render(data, "label"),
				Badge = WorldPlaceData.Render(data, "badge"),
				Image = WorldPlaceData.Render(data, "image"),
				Info = WorldPlaceData.Info(data),
			});
		}

		bool HiddenAt(int x, int y) =>
			!examine && WorldPlaceData.Hidden(WorldPlaceData.Parse(read.Places.GetValueOrDefault((x, y))?.Data));

		var walls = new List<MapWallDto>();
		foreach (var ((x, y), cell) in read.Cells)
		{
			if (cell.Edges == 0 || HiddenAt(x, y)) continue;
			var reasons = read.Places.GetValueOrDefault((x, y))?.Reasons;
			foreach (var owned in WorldGrid.Owned(space))
			{
				var (dx, dy, _) = WorldGrid.Step(owned);
				if (Inside(x, y) || (owned != WorldSide.Up && Inside(x + dx, y + dy))) Add(x, y, owned);
			}

			void Add(int ax, int ay, WorldSide owned)
			{
				var edge = WorldGrid.Edge(cell.Edges, owned);
				var reason = reasons?.GetValueOrDefault(WorldGrid.Letter(owned));
				var (dx, dy, _) = WorldGrid.Step(owned);
				if (owned != WorldSide.Up && HiddenAt(x + dx, y + dy)) return;
				switch (edge)
				{
					case WorldEdge.Wall:
						walls.Add(new MapWallDto(ax, ay, WorldGrid.Letter(owned), MapWallKinds.Wall, reason));
						break;
					case WorldEdge.Lock:
						walls.Add(new MapWallDto(ax, ay, WorldGrid.Letter(owned), MapWallKinds.Lock, reason));
						break;
					case WorldEdge.OneWayOut:
						walls.Add(new MapWallDto(ax, ay, WorldGrid.Letter(owned), MapWallKinds.OneWay, reason));
						break;
					// One way in from above is the level above's to draw, as a way down.
					case WorldEdge.OneWayIn when owned != WorldSide.Up:
						walls.Add(new MapWallDto(ax + dx, ay + dy, WorldGrid.Letter(WorldGrid.Opposite(owned)), MapWallKinds.OneWay, reason));
						break;
				}
			}
		}

		// A place's down side is the up side of the place below it, which the level below holds.
		if (store.Window(dbref, level - 1, left, bottom, right, top) is { } below)
		{
			foreach (var ((x, y), cell) in below.Cells)
			{
				if (!Inside(x, y) || cell.Edges == 0) continue;
				if (!examine && WorldPlaceData.Hidden(WorldPlaceData.Parse(below.Places.GetValueOrDefault((x, y))?.Data))) continue;
				var reason = below.Places.GetValueOrDefault((x, y))?.Reasons?.GetValueOrDefault(WorldGrid.Letter(WorldSide.Up));
				var kind = WorldGrid.Edge(cell.Edges, WorldSide.Up) switch
				{
					WorldEdge.Wall => MapWallKinds.Wall,
					WorldEdge.Lock => MapWallKinds.Lock,
					// One way in from above leads down out of this place.
					WorldEdge.OneWayIn => MapWallKinds.OneWay,
					_ => null,
				};
				if (kind is not null) walls.Add(new MapWallDto(x, y, WorldGrid.Letter(WorldSide.Down), kind, reason));
			}
		}

		return empty with { Cells = cells, Walls = walls };
	}
}
