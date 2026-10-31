using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.World.Common;
using SharpMUSH.Plugins.World.Storage;

namespace SharpMUSH.Tests.WorldPlugin;

/// <summary>The World plugin's store against a real LMDB world: maps, painted tiles, shared edges and place records.</summary>
public class WorldStoreTests
{
	private string _path = null!;
	private LightningDatabase _db = null!;
	private WorldStore _store = null!;
	private static readonly DBRef Map = new(4200, 1000);

	[Before(Test)]
	public async Task Open()
	{
		_path = Path.Join(Path.GetTempPath(), "sharpmush-world-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
		_store = new WorldStore(_db);
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

	[Test]
	public async Task AMapIsCreatedOnceAndBelongsToItsObjectAlone()
	{
		await Assert.That(await _store.CreateAsync(Map, "square")).IsTrue();
		await Assert.That(await _store.CreateAsync(Map, "square")).IsFalse();
		await Assert.That(_store.Map(Map)?.Space).IsEqualTo("square");

		// A later object with the same number is a different object.
		var next = new DBRef(Map.Number, 2000);
		await Assert.That(_store.Map(next)).IsNull();
		await Assert.That(await _store.CreateAsync(next, "square")).IsTrue();
		await Assert.That(_store.Map(Map)).IsNull();
	}

	[Test]
	public async Task APaintAcrossTilesCoversEveryPlaceAndAnEraseDropsTheTiles()
	{
		await _store.CreateAsync(Map, "square");
		await Assert.That(await _store.PaintAsync(Map, 0, -3, -3, 40, 2, "forest")).IsEqualTo(44L * 6);
		await _store.PaintAsync(Map, 0, 5, 0, 5, 0, "river");

		var record = _store.Map(Map)!;
		await Assert.That(record.Palette).IsEquivalentTo(["forest", "river"]);
		await Assert.That((record.Left, record.Bottom, record.Right, record.Top)).IsEqualTo((-3, -3, 40, 2));
		await Assert.That(record.Levels).IsEquivalentTo([0]);
		await Assert.That(record.Type(_store.Cell(Map, -3, -3, 0).Terrain)).IsEqualTo("forest");
		await Assert.That(record.Type(_store.Cell(Map, 40, 2, 0).Terrain)).IsEqualTo("forest");
		await Assert.That(record.Type(_store.Cell(Map, 5, 0, 0).Terrain)).IsEqualTo("river");
		await Assert.That(record.Type(_store.Cell(Map, 41, 2, 0).Terrain)).IsNull();
		await Assert.That(record.Type(_store.Cell(Map, 0, 0, 1).Terrain)).IsNull();

		await _store.PaintAsync(Map, 0, -3, -3, 40, 2, null);
		await Assert.That(_store.Window(Map, 0, -10, -10, 50, 10)!.Cells).IsEmpty();
	}

	[Test]
	public async Task AnEdgeIsSharedByThePlacesOnEitherSide()
	{
		await _store.CreateAsync(Map, "square");
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.South, WorldEdge.Wall, "Cliff");

		await Assert.That(_store.Edge(Map, 0, 0, 0, WorldSide.South)).IsEqualTo((WorldEdge.Wall, (string?)"Cliff", (string?)null));
		await Assert.That(_store.Edge(Map, 0, -1, 0, WorldSide.North)).IsEqualTo((WorldEdge.Wall, (string?)"Cliff", (string?)null));
		await Assert.That(_store.Edge(Map, 0, 0, 0, WorldSide.North).Edge).IsEqualTo(WorldEdge.Open);

		// One way out of 0,0 westward is one way in, seen from 1 west.
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.West, WorldEdge.OneWayOut, null);
		await Assert.That(_store.Edge(Map, 0, 0, 0, WorldSide.West).Edge).IsEqualTo(WorldEdge.OneWayOut);
		await Assert.That(_store.Edge(Map, -1, 0, 0, WorldSide.East).Edge).IsEqualTo(WorldEdge.OneWayIn);

		// Opening a side drops its reason, and the place record goes with it.
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.South, WorldEdge.Open, "ignored");
		await Assert.That(_store.Edge(Map, 0, -1, 0, WorldSide.North)).IsEqualTo((WorldEdge.Open, (string?)null, (string?)null));
		await Assert.That(_store.Place(Map, 0, -1, 0)).IsNull();
	}

	[Test]
	public async Task AHexPlaceStoresItsNorthNortheastAndSoutheastEdges()
	{
		await _store.CreateAsync(Map, "hex");
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.SouthWest, WorldEdge.Wall, "Brambles");
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.NorthWest, WorldEdge.Lock, "A gate");
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.North, WorldEdge.OneWayOut, null);

		await Assert.That(_store.Edge(Map, -1, 0, 0, WorldSide.NorthEast)).IsEqualTo((WorldEdge.Wall, (string?)"Brambles", (string?)null));
		await Assert.That(_store.Edge(Map, -1, 1, 0, WorldSide.SouthEast)).IsEqualTo((WorldEdge.Lock, (string?)"A gate", (string?)null));
		await Assert.That(_store.Place(Map, -1, 1, 0)!.Reasons).IsEquivalentTo(new Dictionary<string, string> { ["se"] = "A gate" });
		await Assert.That(_store.Edge(Map, 0, 1, 0, WorldSide.South).Edge).IsEqualTo(WorldEdge.OneWayIn);
		var edges = _store.Cell(Map, -1, 0, 0).Edges;
		await Assert.That((WorldGrid.Edge(edges, WorldSide.NorthEast), WorldGrid.Edge(edges, WorldSide.North), WorldGrid.Edge(edges, WorldSide.East)))
			.IsEqualTo((WorldEdge.Wall, WorldEdge.Open, WorldEdge.Open)).Because("each owned edge has bits of its own");
	}

	[Test]
	public async Task APointyHexPlaceStoresItsNortheastEastAndSoutheastEdges()
	{
		await _store.CreateAsync(Map, "pointy-hex");
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.PointySouthWest, WorldEdge.Wall, "Brambles");
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.West, WorldEdge.Lock, "A gate");
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.NorthWest, WorldEdge.OneWayOut, null);

		await Assert.That(_store.Edge(Map, 0, -1, 0, WorldSide.PointyNorthEast)).IsEqualTo((WorldEdge.Wall, (string?)"Brambles", (string?)null));
		await Assert.That(_store.Place(Map, 0, -1, 0)!.Reasons).IsEquivalentTo(new Dictionary<string, string> { ["ne"] = "Brambles" });
		await Assert.That(_store.Edge(Map, -1, 0, 0, WorldSide.East)).IsEqualTo((WorldEdge.Lock, (string?)"A gate", (string?)null));
		await Assert.That(_store.Edge(Map, -1, 1, 0, WorldSide.SouthEast).Edge).IsEqualTo(WorldEdge.OneWayIn);
		await Assert.That(WorldGrid.ParseSide("ne", "pointy-hex")).IsEqualTo(WorldSide.PointyNorthEast);
		await Assert.That(WorldGrid.ParseSide("n", "pointy-hex")).IsNull().Because("a pointy place's north is a corner");
	}

	[Test]
	public async Task AGraphPlaceKeepsItsWaysByNameAndTheyGoWithTheMap()
	{
		await _store.CreateAsync(Map, "graph");
		await Assert.That(await _store.LinkAsync(Map, (0, 0, 0), (4, 2, 1), "Old Road;road", "Back Road;back")).IsEqualTo(WorldLinking.Linked);
		await _store.LinkAsync(Map, (0, 0, 0), (7, 7, 0), "Ford", null);

		await Assert.That(_store.Links(Map, 0, 0, 0).Select(link => link.Way)).IsEquivalentTo(["ford", "old_road"]);
		await Assert.That(_store.Link(Map, 0, 0, 0, "ROAD")).IsEqualTo(new WorldLinkRecord("Old Road;road", 4, 2, 1, WorldEdge.Open, null, null));
		await Assert.That(_store.Link(Map, 4, 2, 1, "back_road")!.Z).IsEqualTo(0);
		await Assert.That(_store.AllLinks(Map).Select(entry => (entry.From, entry.Link.Way)))
			.IsEquivalentTo([(new WorldPosition(0, 0, 0), "ford"), (new WorldPosition(0, 0, 0), "old_road"), (new WorldPosition(4, 2, 1), "back_road")]);

		await _store.ChangeLinkAsync(Map, 0, 0, 0, "road", link => link with { Edge = WorldEdge.Lock, Key = "#FALSE" });
		await _store.LinkAsync(Map, (0, 0, 0), (5, 5, 0), "old road", null);
		await Assert.That(_store.Link(Map, 0, 0, 0, "old_road")).IsEqualTo(new WorldLinkRecord("old road", 5, 5, 0, WorldEdge.Open, null, null))
			.Because("a way of the same name is replaced, lock and all");
		await Assert.That(await _store.LinkAsync(Map, (0, 0, 0), (6, 6, 0), "Mill Lane;FORD", null)).IsEqualTo(WorldLinking.NameTaken)
			.Because("no two ways out of one place answer to one name");
		await Assert.That(await _store.LinkAsync(Map, (7, 7, 0), (0, 0, 0), "Ford Back", "Mill Lane;old  road")).IsEqualTo(WorldLinking.NameTaken)
			.Because("the way back is held to it too, its names keyed as a way's are");
		await Assert.That(_store.Links(Map, 7, 7, 0)).IsEmpty().Because("a refused link makes neither way");
		await Assert.That(await _store.ChangeLinkAsync(Map, 0, 0, 0, "ford", _ => null)).IsTrue();
		await Assert.That(await _store.ChangeLinkAsync(Map, 0, 0, 0, "ford", _ => null)).IsFalse();

		await _store.ObjectsFreedAsync([Map]);
		await _store.CreateAsync(Map, "graph");
		await Assert.That(_store.AllLinks(Map)).IsEmpty();
	}

	[Test]
	public async Task AGraphPlaceHasAtMostSoManyWaysOut()
	{
		await _store.CreateAsync(Map, "graph");
		for (var i = 0; i < WorldLinks.MaxWays; i++) await _store.LinkAsync(Map, (0, 0, 0), (i, 1, 0), $"way{i}", null);

		await Assert.That(await _store.LinkAsync(Map, (0, 0, 0), (0, 2, 0), "one more", null)).IsEqualTo(WorldLinking.TooMany);
		await Assert.That(await _store.LinkAsync(Map, (0, 0, 0), (0, 2, 0), "way0", null)).IsEqualTo(WorldLinking.Linked)
			.Because("replacing a way adds none");
		await Assert.That(WorldLinks.Normalize(" Old Road ; road ;")).IsEqualTo("Old Road;road");
		await Assert.That(WorldLinks.Normalize("a,b")).IsNull();
	}

	[Test]
	public async Task AWindowHoldsItsPlacesAndTheEdgesOnItsBorder()
	{
		await _store.CreateAsync(Map, "square");
		await _store.PaintAsync(Map, 0, 0, 0, 3, 3, "grass");
		await _store.SetEdgeAsync(Map, 0, 0, 0, WorldSide.West, WorldEdge.Wall, null);
		await _store.ChangePlaceAsync(Map, 1, 1, 0, place => place with { Name = "Well" });
		await _store.ChangePlaceAsync(Map, 9, 9, 0, place => place with { Name = "Outside" });

		var window = _store.Window(Map, 0, 0, 0, 3, 3)!;
		await Assert.That(window.Cells.Count).IsEqualTo(17);
		await Assert.That(window.Cells.ContainsKey((-1, 0))).IsTrue();
		await Assert.That(window.Places.Keys).IsEquivalentTo([(1, 1)]);
		await Assert.That(window.Places[(1, 1)].Name).IsEqualTo("Well");
	}

	[Test]
	public async Task AFreedMapTakesItsPlacesWithIt()
	{
		await _store.CreateAsync(Map, "square");
		await _store.PaintAsync(Map, 0, 0, 0, 0, 0, "grass");
		await _store.ChangePlaceAsync(Map, 0, 0, 0, place => place with { Name = "Here" });

		await _store.ObjectsFreedAsync([Map]);

		await Assert.That(_store.Map(Map)).IsNull();
		await _store.CreateAsync(Map, "square");
		await Assert.That(_store.Cell(Map, 0, 0, 0).IsEmpty).IsTrue();
		await Assert.That(_store.Place(Map, 0, 0, 0)).IsNull();
	}

	[Test]
	public async Task AnEraseShrinksTheMapToWhatIsStillPainted()
	{
		await _store.CreateAsync(Map, "square");
		await _store.PaintAsync(Map, 0, 0, 0, 3, 3, "grass");
		await _store.PaintAsync(Map, 0, 500, -40, 500, -40, "rock");
		await _store.PaintAsync(Map, 2, 1, 1, 1, 1, "cave");

		await _store.PaintAsync(Map, 0, 500, -40, 500, -40, null);
		var record = _store.Map(Map)!;
		await Assert.That((record.Left, record.Bottom, record.Right, record.Top)).IsEqualTo((0, 0, 3, 3));
		await Assert.That(record.Levels).IsEquivalentTo([0, 2]);

		await _store.PaintAsync(Map, 2, 1, 1, 1, 1, null);
		await Assert.That(_store.Map(Map)!.Levels).IsEquivalentTo([0]);

		await _store.PaintAsync(Map, 0, 0, 0, 3, 3, null);
		await Assert.That(_store.Map(Map)!.Painted).IsFalse();
		await Assert.That(_store.Map(Map)!.Levels).IsEmpty();
	}

	[Test]
	public async Task AWindowAtTheEdgeOfTheIntegersReadsNothingAndEnds()
	{
		await _store.CreateAsync(Map, "square");
		await _store.PaintAsync(Map, 0, 0, 0, 0, 0, "grass");

		var far = _store.Window(Map, 0, int.MaxValue, 0, int.MaxValue, 0)!;
		await Assert.That(far.Cells).IsEmpty();
		var corner = _store.Window(Map, 0, int.MinValue, int.MinValue, -WorldGrid.Reach + 1, -WorldGrid.Reach + 1)!;
		await Assert.That(corner.Cells).IsEmpty();
	}
}
