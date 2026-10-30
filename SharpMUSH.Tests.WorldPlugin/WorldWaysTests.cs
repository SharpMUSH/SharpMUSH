using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Tests.WorldPlugin;

/// <summary>How a mover fares going one way out of a place: sides, one-way edges, closed terrain and diagonals.</summary>
public class WorldWaysTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private WorldStore _store = null!;
	private static readonly DBRef Map = new(4300, 1000);

	/// <summary>A legend whose lava is closed.</summary>
	private static readonly WorldLegend Legend = new(name =>
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

	private async Task<WorldWay> Way(int x, int y, string direction, bool examine = true) =>
		await WorldWays.WayAsync(_store, _store.Map(Map)!, Legend, Map, examine, new WorldPosition(x, y, 0), WorldWays.ParseDirection(direction)!);

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
}
