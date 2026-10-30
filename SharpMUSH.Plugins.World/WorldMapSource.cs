using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World;

/// <summary>
/// The World map's view of the stored maps: the space a map's places are in, and a window of them as a viewer may see
/// them. Reads only. A place whose data hides it (<c>render.hidden</c>) is left out for a viewer who may not examine
/// the map.
/// </summary>
public sealed class WorldMapSource(WorldStore store, IAttributeService attributes, IPermissionService permissions) : IWorldMapSource
{
	public ValueTask<Found<MapSpaceDto>> SpaceAsync(AnySharpObject map, CancellationToken ct) =>
		ValueTask.FromResult<Found<MapSpaceDto>>(store.Map(map.Object().DBRef) is { } record
			? new MapSpaceDto(record.Space, record.Left, record.Bottom, record.Right, record.Top, record.Levels)
			: new NotFound());

	public async ValueTask<MapCellsDto> CellsAsync(AnySharpObject viewer, AnySharpObject map, MapWindow window, CancellationToken ct)
	{
		var (level, left, bottom, right, top) = (window.Level, window.Left, window.Bottom, window.Right, window.Top);
		var empty = new MapCellsDto(level, left, bottom, right, top, [], [], false);
		if (window.Cells > MapWindow.MaxCells) return empty with { Truncated = true };
		var dbref = map.Object().DBRef;
		if (store.Window(dbref, level, left, bottom, right, top) is not { } read) return empty;

		var examine = await permissions.CanExamine(viewer, map);
		var legend = new WorldLegend(attributes, map);
		bool Inside(int x, int y) => x >= left && x <= right && y >= bottom && y <= top;

		var cells = new List<MapCellDto>();
		foreach (var ((x, y), cell) in read.Cells.OrderByDescending(entry => entry.Key.Y).ThenBy(entry => entry.Key.X))
		{
			if (!Inside(x, y) || read.Map.Type(cell.Terrain) is not { } type) continue;
			var place = read.Places.GetValueOrDefault((x, y));
			var data = WorldPlaceData.Parse(place?.Data);
			if (!examine && WorldPlaceData.Hidden(data)) continue;
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

		var walls = new List<MapWallDto>();
		foreach (var ((x, y), cell) in read.Cells)
		{
			if (cell.Edges == 0) continue;
			var reasons = read.Places.GetValueOrDefault((x, y))?.Reasons;
			if (Inside(x, y) || Inside(x, y + 1)) Add(x, y, WorldSide.North);
			if (Inside(x, y) || Inside(x + 1, y)) Add(x, y, WorldSide.East);
			if (Inside(x, y)) Add(x, y, WorldSide.Up);

			void Add(int ax, int ay, WorldSide owned)
			{
				var edge = WorldGrid.Edge(cell.Edges, owned);
				var reason = reasons?.GetValueOrDefault(WorldGrid.Letter(owned));
				var (dx, dy, _) = WorldGrid.Step(owned);
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
						walls.Add(new MapWallDto(ax + dx, ay + dy, WorldGrid.Letter(Opposite(owned)), MapWallKinds.OneWay, reason));
						break;
				}
			}
		}

		return empty with { Cells = cells, Walls = walls };
	}

	private static WorldSide Opposite(WorldSide side) => side switch
	{
		WorldSide.North => WorldSide.South,
		WorldSide.East => WorldSide.West,
		WorldSide.South => WorldSide.North,
		WorldSide.West => WorldSide.East,
		WorldSide.Up => WorldSide.Down,
		_ => WorldSide.Up,
	};
}
