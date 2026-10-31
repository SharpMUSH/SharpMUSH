using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Tests.WorldPlugin;

/// <summary>How a mover fares going one way out of a place: sides, one-way edges, locks, closed terrain and diagonals.</summary>
public class WorldWaysTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private WorldStore _store = null!;
	private static readonly DBRef Map = new(4300, 1000);

	/// <summary>A legend whose lava is closed. One per test: a legend caches what it read and is not shared.</summary>
	private readonly WorldLegend _legend = new(name =>
		ValueTask.FromResult(name == "MAP`LEGEND`LAVA`CLOSED" ? "Too hot to cross." : null));

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-world-ways-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_store = new WorldStore(_db);
		await _store.CreateAsync(Map, "square");
		await _store.PaintAsync(Map, 0, 0, 0, 2, 2, "grass");
	}

	[After(Test)]
	public async Task Close()
	{
		await _db.DisposeAsync();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: a lingering mdb.lck can outlive the writer join.
		}
	}

	private async Task<WorldWay> Way(int x, int y, string direction, bool examine = true, Func<string, ValueTask<bool>>? unlock = null,
		WorldLegend? legend = null) =>
		await WorldWays.WayAsync(_store, _store.Map(Map)!, legend ?? _legend, Map, examine, new WorldPosition(x, y, 0),
			WorldWays.ParseDirection(direction, _store.Map(Map)!.Space)!, unlock);

	/// <summary>A mover who passes exactly the keys given.</summary>
	private static Func<string, ValueTask<bool>> Holding(params string[] keys) => key => ValueTask.FromResult(keys.Contains(key));

	[Test]
	public async Task AHexPlaceHasSixSidesAndNoDiagonals()
	{
		var hex = new DBRef(4301, 1000);
		await _store.CreateAsync(hex, "hex");
		await _store.PaintAsync(hex, 0, -2, -2, 2, 2, "grass");
		await _store.SetEdgeAsync(hex, 0, 0, 0, WorldSide.SouthWest, WorldEdge.Wall, "A thorn hedge.");
		await _store.SetEdgeAsync(hex, 0, 0, 0, WorldSide.NorthWest, WorldEdge.OneWayOut, null);
		var record = _store.Map(hex)!;

		async Task<WorldWay> HexWay(int x, int y, string direction) =>
			await WorldWays.WayAsync(_store, record, _legend, hex, true, new WorldPosition(x, y, 0),
				WorldWays.ParseDirection(direction, record.Space)!);

		await Assert.That((await HexWay(0, 0, "ne")).To).IsEqualTo(new WorldPosition(1, 0, 0));
		await Assert.That((await HexWay(0, 0, "se")).To).IsEqualTo(new WorldPosition(1, -1, 0));
		await Assert.That((await HexWay(0, 0, "n")).To).IsEqualTo(new WorldPosition(0, 1, 0));
		await Assert.That(await HexWay(0, 0, "sw")).IsEqualTo(new WorldWay(WorldWayState.Wall, "A thorn hedge.", new WorldPosition(-1, 0, 0)));
		await Assert.That((await HexWay(-1, 0, "ne")).State).IsEqualTo(WorldWayState.Wall).Because("the hedge is the same edge seen from the other side");
		await Assert.That((await HexWay(0, 0, "nw")).State).IsEqualTo(WorldWayState.OneWay);
		await Assert.That((await HexWay(-1, 1, "se")).State).IsEqualTo(WorldWayState.Wall).Because("a one-way side refuses the way back");
		await Assert.That(WorldWays.ParseDirection("e", record.Space)).IsNull().Because("a hex place has no east side");
		await Assert.That(WorldWays.ParseDirection("ne", "square")).IsEquivalentTo([WorldSide.North, WorldSide.East])
			.Because("a square map's northeast is still a diagonal");
	}

	[Test]
	[Arguments("square", 10)]
	[Arguments("hex", 8)]
	public async Task EveryDirectionOfASpaceIsAWayOutOfItsPlaces(string space, int count)
	{
		var directions = WorldWays.Directions(space);
		await Assert.That(directions.Count).IsEqualTo(count);
		foreach (var direction in directions)
			await Assert.That(WorldWays.ParseDirection(direction, space)).IsNotNull().Because($"{direction} is a {space} direction");
	}

	[Test]
	public async Task AnOpenSideLeadsToThePlaceBesideIt()
	{
		var way = await Way(1, 1, "n");
		await Assert.That(way.StateWord).IsEqualTo("open");
		await Assert.That(way.To).IsEqualTo(new WorldPosition(1, 2, 0));
	}

	[Test]
	public async Task NothingPaintedThatWayIsNone()
	{
		await Assert.That((await Way(2, 2, "e")).State).IsEqualTo(WorldWayState.None);
		await Assert.That((await Way(1, 1, "u")).State).IsEqualTo(WorldWayState.None);
	}

	[Test]
	public async Task AOneWaySideIsAWallComingBack()
	{
		await _store.SetEdgeAsync(Map, 1, 1, 0, WorldSide.East, WorldEdge.OneWayOut, "A drop.");
		await Assert.That((await Way(1, 1, "e")).StateWord).IsEqualTo("oneway");
		var back = await Way(2, 1, "w");
		await Assert.That(back.State).IsEqualTo(WorldWayState.Wall);
		await Assert.That(back.Reason).IsEqualTo("A drop.");
	}

	[Test]
	public async Task ClosedTerrainRefusesWithItsReason()
	{
		await _store.PaintAsync(Map, 0, 1, 2, 1, 2, "lava");
		var way = await Way(1, 1, "north");
		await Assert.That(way.State).IsEqualTo(WorldWayState.Closed);
		await Assert.That(way.Reason).IsEqualTo("Too hot to cross.");
	}

	[Test]
	public async Task ADiagonalTakesTheBetterOfItsTwoRoutes()
	{
		await _store.SetEdgeAsync(Map, 1, 1, 0, WorldSide.North, WorldEdge.Wall, null);
		var around = await Way(1, 1, "ne");
		await Assert.That(around.State).IsEqualTo(WorldWayState.Open);
		await Assert.That(around.To).IsEqualTo(new WorldPosition(2, 2, 0));

		await _store.SetEdgeAsync(Map, 1, 1, 0, WorldSide.East, WorldEdge.Wall, "Fence.");
		await Assert.That((await Way(1, 1, "ne")).State).IsEqualTo(WorldWayState.Wall);
	}

	[Test]
	public async Task AHiddenPlaceHasNoWaysForAReaderWhoMayNotExamine()
	{
		await _store.ChangePlaceAsync(Map, 1, 2, 0, place => place with { Data = """{"render":{"hidden":true}}""" });
		await _store.SetEdgeAsync(Map, 1, 1, 0, WorldSide.North, WorldEdge.Wall, "A hedge.");

		await Assert.That((await Way(1, 1, "n")).State).IsEqualTo(WorldWayState.Wall);
		var hidden = await Way(1, 1, "n", examine: false);
		await Assert.That((hidden.State, hidden.Reason)).IsEqualTo((WorldWayState.None, (string?)null));
		await Assert.That((await Way(1, 2, "s", examine: false)).State).IsEqualTo(WorldWayState.None);
		await Assert.That((await Way(0, 1, "ne", examine: false)).To).IsEqualTo(new WorldPosition(1, 2, 0));
		await Assert.That((await Way(0, 1, "ne", examine: false)).State).IsEqualTo(WorldWayState.None);
		await Assert.That((await Way(0, 2, "se", examine: false)).State)
			.IsEqualTo(WorldWayState.Open).Because("the route by 0,1 avoids the hidden place");
	}

	[Test]
	public async Task ALockedSideOpensForAMoverWhoPassesItsKey()
	{
		await _store.SetEdgeAsync(Map, 1, 1, 0, WorldSide.North, WorldEdge.Lock, "The gate is shut.");
		await _store.SetLockAsync(Map, 1, 1, 0, WorldSide.North, "#123");

		var unknown = await Way(1, 1, "n");
		await Assert.That((unknown.State, unknown.Reason)).IsEqualTo((WorldWayState.Lock, (string?)"The gate is shut."));
		var refused = await Way(1, 1, "n", unlock: Holding("#456"));
		await Assert.That((refused.State, refused.Reason)).IsEqualTo((WorldWayState.Lock, (string?)"The gate is shut."));
		var passed = await Way(1, 2, "s", unlock: Holding("#123"));
		await Assert.That((passed.State, passed.Reason, passed.To)).IsEqualTo((WorldWayState.Open, (string?)null, new WorldPosition(1, 1, 0)));
	}

	[Test]
	public async Task ASideLockedWithNoKeyLetsNobodyThrough()
	{
		await _store.SetEdgeAsync(Map, 1, 1, 0, WorldSide.North, WorldEdge.Lock, null);
		await Assert.That((await Way(1, 1, "n", unlock: _ => ValueTask.FromResult(true))).State).IsEqualTo(WorldWayState.Open);
		await Assert.That((await Way(1, 1, "n", unlock: Holding("#1"))).State).IsEqualTo(WorldWayState.Lock);
	}

	[Test]
	public async Task OpeningALockedSideDropsItsKeyAndReason()
	{
		await _store.SetEdgeAsync(Map, 1, 1, 0, WorldSide.East, WorldEdge.Lock, "Barred.");
		await _store.SetLockAsync(Map, 1, 1, 0, WorldSide.East, "#123");
		await Assert.That(_store.Edge(Map, 2, 1, 0, WorldSide.West)).IsEqualTo((WorldEdge.Lock, (string?)"Barred.", (string?)"#123"));

		await _store.SetEdgeAsync(Map, 1, 1, 0, WorldSide.East, WorldEdge.Wall, "Bricked up.");
		await Assert.That(_store.Edge(Map, 1, 1, 0, WorldSide.East)).IsEqualTo((WorldEdge.Wall, (string?)"Bricked up.", (string?)null));

		await _store.SetLockAsync(Map, 1, 1, 0, WorldSide.East, "#123");
		await _store.SetLockAsync(Map, 1, 1, 0, WorldSide.East, null);
		await Assert.That(_store.Edge(Map, 1, 1, 0, WorldSide.East)).IsEqualTo((WorldEdge.Open, (string?)null, (string?)null));
		await Assert.That(_store.Place(Map, 1, 1, 0)).IsNull();
	}

	[Test]
	public async Task TheLegendLocksMovingIntoATerrainFromAnother()
	{
		await _store.PaintAsync(Map, 0, 1, 2, 2, 2, "mountain");
		var attributes = new Dictionary<string, string>
		{
			["MAP`LEGEND`MOUNTAIN`LOCK"] = "#FALSE",
			["MAP`LEGEND`MOUNTAIN`FAILURE"] = "The slope is too steep.",
		};
		WorldLegend Legend() => new(name => ValueTask.FromResult(attributes.GetValueOrDefault(name)));
		var climber = Holding("CLIMB:1");

		var refused = await Way(1, 1, "n", unlock: climber, legend: Legend());
		await Assert.That((refused.State, refused.Reason)).IsEqualTo((WorldWayState.Lock, (string?)"The slope is too steep."));
		await Assert.That((await Way(1, 1, "n", legend: Legend())).State).IsEqualTo(WorldWayState.Lock);
		await Assert.That((await Way(1, 2, "e", unlock: climber, legend: Legend())).State)
			.IsEqualTo(WorldWayState.Open).Because("the lock is on coming from another terrain");
		await Assert.That((await Way(1, 2, "s", unlock: climber, legend: Legend())).State).IsEqualTo(WorldWayState.Open);

		attributes["MAP`LEGEND`MOUNTAIN`LOCK`GRASS"] = "CLIMB:1";
		attributes["MAP`LEGEND`MOUNTAIN`FAILURE`GRASS"] = "You need climbing gear.";
		await Assert.That((await Way(1, 1, "n", unlock: climber, legend: Legend())).State).IsEqualTo(WorldWayState.Open);
		var unequipped = await Way(1, 1, "n", unlock: Holding(), legend: Legend());
		await Assert.That((unequipped.State, unequipped.Reason)).IsEqualTo((WorldWayState.Lock, (string?)"You need climbing gear."));

		attributes["MAP`LEGEND`MOUNTAIN`LOCK`MOUNTAIN"] = "#FALSE";
		await Assert.That((await Way(1, 2, "e", unlock: climber, legend: Legend())).State)
			.IsEqualTo(WorldWayState.Lock).Because("a key for the terrain itself gates moving within it");
	}

	[Test]
	public async Task ADiagonalGoesRoundATerrainLockTheMoverFails()
	{
		await _store.PaintAsync(Map, 0, 1, 2, 1, 2, "mountain");
		WorldLegend Legend() => new(name => ValueTask.FromResult(name == "MAP`LEGEND`MOUNTAIN`LOCK" ? "#FALSE" : null));
		var around = await Way(1, 1, "ne", unlock: Holding(), legend: Legend());
		await Assert.That((around.State, around.To)).IsEqualTo((WorldWayState.Open, new WorldPosition(2, 2, 0)));
	}
}
