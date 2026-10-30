using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Plugins.World.Common;

/// <summary>How a mover fares going one way out of a place, best first.</summary>
public enum WorldWayState
{
	/// <summary>The way is open.</summary>
	Open,

	/// <summary>The way is open going out, and not coming back.</summary>
	OneWay,

	/// <summary>Only whoever passes its lock goes through.</summary>
	Lock,

	/// <summary>The place it leads to cannot be entered: its terrain is closed.</summary>
	Closed,

	/// <summary>Nothing passes this way.</summary>
	Wall,

	/// <summary>There is no place that way.</summary>
	None,
}

/// <summary>One way out of a place: how a mover fares, why when they cannot, and where it leads.</summary>
public sealed record WorldWay(WorldWayState State, string? Reason, WorldPosition To)
{
	public string StateWord => State switch
	{
		WorldWayState.OneWay => "oneway",
		_ => State.ToString().ToLowerInvariant(),
	};
}

/// <summary>A way out of a place, by side or by compass point.</summary>
public static class WorldWays
{
	/// <summary>The compass points: a side, or two sides at once for a diagonal.</summary>
	public static IReadOnlyList<WorldSide>? ParseDirection(string text) => text.Trim().ToLowerInvariant() switch
	{
		"ne" or "northeast" => [WorldSide.North, WorldSide.East],
		"nw" or "northwest" => [WorldSide.North, WorldSide.West],
		"se" or "southeast" => [WorldSide.South, WorldSide.East],
		"sw" or "southwest" => [WorldSide.South, WorldSide.West],
		var other => WorldGrid.ParseSide(other) is { } side ? [side] : null,
	};

	/// <summary>
	/// The way out of <paramref name="from"/> toward <paramref name="direction"/>. A diagonal goes round a corner: it is
	/// as good as the better of its two routes, each as good as its worse step.
	/// </summary>
	public static async ValueTask<WorldWay> WayAsync(WorldStore store, WorldMapRecord record, WorldLegend legend, DBRef map,
		WorldPosition from, IReadOnlyList<WorldSide> direction)
	{
		if (direction is [var side]) return await StepAsync(store, record, legend, map, from, side);

		var (first, second) = (direction[0], direction[1]);
		var target = from.Toward(first).Toward(second);
		var one = await RouteAsync(store, record, legend, map, from, first, second);
		var other = await RouteAsync(store, record, legend, map, from, second, first);
		var best = one.State <= other.State ? one : other;
		return best with { To = target };
	}

	private static async ValueTask<WorldWay> RouteAsync(WorldStore store, WorldMapRecord record, WorldLegend legend, DBRef map,
		WorldPosition from, WorldSide first, WorldSide second)
	{
		var step = await StepAsync(store, record, legend, map, from, first);
		if (step.State > WorldWayState.Lock) return step;
		var next = await StepAsync(store, record, legend, map, step.To, second);
		return next.State >= step.State ? next : step with { To = next.To };
	}

	/// <summary>One step out of <paramref name="from"/> across its <paramref name="side"/>.</summary>
	public static async ValueTask<WorldWay> StepAsync(WorldStore store, WorldMapRecord record, WorldLegend legend, DBRef map,
		WorldPosition from, WorldSide side)
	{
		var to = from.Toward(side);
		var (edge, reason) = store.Edge(map, from.X, from.Y, from.Z, side);
		var state = edge switch
		{
			WorldEdge.Wall or WorldEdge.OneWayIn => WorldWayState.Wall,
			WorldEdge.OneWayOut => WorldWayState.OneWay,
			WorldEdge.Lock => WorldWayState.Lock,
			_ => WorldWayState.Open,
		};
		if (state == WorldWayState.Wall) return new WorldWay(state, reason, to);
		if (!WorldGrid.InReach(to.X, to.Y, to.Z) || record.Type(store.Cell(map, to.X, to.Y, to.Z).Terrain) is not { } type)
			return new WorldWay(WorldWayState.None, null, to);
		return (await legend.TerrainAsync(type)).Closed is { } closed
			? new WorldWay(WorldWayState.Closed, closed, to)
			: new WorldWay(state, reason, to);
	}
}
