using SharpMUSH.Library.API;
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

	/// <summary>Only whoever passes its lock goes through; for a known mover, one who does not.</summary>
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
	/// <summary>
	/// The directions a place in <paramref name="space"/> has a way out by, by letter: its sides, then the corner steps
	/// (a square map's four diagonals, a flat hex map's east and west, a pointy one's north and south), then up and down.
	/// </summary>
	public static IReadOnlyList<string> Directions(string space) => space switch
	{
		MapSpaces.Hex => ["n", "ne", "se", "s", "sw", "nw", "e", "w", "u", "d"],
		MapSpaces.PointyHex => ["ne", "e", "se", "sw", "w", "nw", "n", "s", "u", "d"],
		_ => ["n", "e", "s", "w", "ne", "nw", "se", "sw", "u", "d"],
	};

	/// <summary>
	/// A way out of a place in <paramref name="space"/>: one of its sides, or a corner step, two sides at once round the
	/// corner between them. A square map's corner steps are its diagonals; a flat hex map's are east and west, which
	/// face a corner and lead to the place two columns over; a pointy hex map's are north and south. Null for a
	/// direction the space has no way out by.
	/// </summary>
	public static IReadOnlyList<WorldSide>? ParseDirection(string text, string space) => (text.Trim().ToLowerInvariant(), space) switch
	{
		_ when WorldGrid.ParseSide(text, space) is { } side => [side],
		("e" or "east", MapSpaces.Hex) => [WorldSide.NorthEast, WorldSide.SouthEast],
		("w" or "west", MapSpaces.Hex) => [WorldSide.NorthWest, WorldSide.SouthWest],
		("n" or "north", MapSpaces.PointyHex) => [WorldSide.PointyNorthEast, WorldSide.NorthWest],
		("s" or "south", MapSpaces.PointyHex) => [WorldSide.SouthEast, WorldSide.PointySouthWest],
		(_, MapSpaces.Hex or MapSpaces.PointyHex) => null,
		("ne" or "northeast", _) => [WorldSide.North, WorldSide.East],
		("nw" or "northwest", _) => [WorldSide.North, WorldSide.West],
		("se" or "southeast", _) => [WorldSide.South, WorldSide.East],
		("sw" or "southwest", _) => [WorldSide.South, WorldSide.West],
		_ => null,
	};

	/// <summary>
	/// The way out of <paramref name="from"/> toward <paramref name="direction"/>. A corner step goes round a corner: it
	/// is as good as the better of its two routes, each as good as its worse step. Given <paramref name="unlock"/>, which
	/// says whether the mover passes a lock key, a lock the mover passes is open; without it, every lock is a lock.
	/// </summary>
	public static async ValueTask<WorldWay> WayAsync(WorldStore store, WorldMapRecord record, WorldLegend legend, DBRef map,
		bool examine, WorldPosition from, IReadOnlyList<WorldSide> direction, Func<string, ValueTask<bool>>? unlock = null)
	{
		// A place hidden from the reader has no ways out that it may learn of.
		if (Hidden(store, map, examine, from)) return new WorldWay(WorldWayState.None, null, Target(from, direction));
		if (direction is [var side]) return await StepAsync(store, record, legend, map, examine, from, side, unlock);

		var (first, second) = (direction[0], direction[1]);
		var target = Target(from, direction);
		var one = await RouteAsync(store, record, legend, map, examine, from, first, second, unlock);
		var other = await RouteAsync(store, record, legend, map, examine, from, second, first, unlock);
		var best = one.State <= other.State ? one : other;
		return best with { To = target };
	}

	private static async ValueTask<WorldWay> RouteAsync(WorldStore store, WorldMapRecord record, WorldLegend legend, DBRef map,
		bool examine, WorldPosition from, WorldSide first, WorldSide second, Func<string, ValueTask<bool>>? unlock)
	{
		var step = await StepAsync(store, record, legend, map, examine, from, first, unlock);
		if (step.State > WorldWayState.Lock) return step;
		var next = await StepAsync(store, record, legend, map, examine, step.To, second, unlock);
		return next.State >= step.State ? next : step with { To = next.To };
	}

	/// <summary>
	/// One step out of <paramref name="from"/> across its <paramref name="side"/>: a wall, a place with nothing painted
	/// or closed terrain refuses; then a locked side, and the legend's lock on moving from this place's terrain into
	/// that one's (<see cref="WorldLegend.LockAsync"/>), each for whoever does not pass it.
	/// </summary>
	public static async ValueTask<WorldWay> StepAsync(WorldStore store, WorldMapRecord record, WorldLegend legend, DBRef map,
		bool examine, WorldPosition from, WorldSide side, Func<string, ValueTask<bool>>? unlock = null)
	{
		var to = from.Toward(side);
		if (Hidden(store, map, examine, to)) return new WorldWay(WorldWayState.None, null, to);
		var (edge, reason, key) = store.Edge(map, from.X, from.Y, from.Z, side);
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
		if ((await legend.TerrainAsync(type)).Closed is { } closed) return new WorldWay(WorldWayState.Closed, closed, to);

		if (state == WorldWayState.Lock)
		{
			// A side locked with no key yet lets nobody through.
			if (unlock is null || !await unlock(key ?? "")) return new WorldWay(state, reason, to);
			(state, reason) = (WorldWayState.Open, null);
		}
		var here = record.Type(store.Cell(map, from.X, from.Y, from.Z).Terrain);
		if (await legend.LockAsync(type, here) is { } terrainLock && (unlock is null || !await unlock(terrainLock.Key)))
			return new WorldWay(WorldWayState.Lock, terrainLock.Reason, to);
		return new WorldWay(state, reason, to);
	}

	private static WorldPosition Target(WorldPosition from, IReadOnlyList<WorldSide> direction) =>
		direction.Aggregate(from, (at, side) => at.Toward(side));

	/// <summary>Whether the place at <paramref name="at"/> is hidden from a reader who may not examine the map.</summary>
	private static bool Hidden(WorldStore store, DBRef map, bool examine, WorldPosition at) =>
		!examine && WorldGrid.InReach(at.X, at.Y, at.Z) && WorldPlaceData.Hidden(WorldPlaceData.Parse(store.Place(map, at.X, at.Y, at.Z)?.Data));
}
