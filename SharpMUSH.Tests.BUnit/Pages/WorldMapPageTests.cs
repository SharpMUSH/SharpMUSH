using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Map;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>The slice of <c>api/maps</c> the /map page reads: a harbour with a door to the city.</summary>
file sealed class WorldMapApiHandler : HttpMessageHandler
{
	public static readonly MapSummaryDto City = new("#51", "City", MapKinds.Graph, null);
	public static readonly MapSummaryDto Harbour = new("#50", "Harbour", MapKinds.Coordinates, "#51");
	public static readonly MapSummaryDto Caves = new("#52", "Caves", MapKinds.Coordinates, "#50");

	public static readonly MapDto HarbourMap = new(Harbour, null,
		[
			new("#10", "Quay", [0, 0, 0], null),
			new("#11", "Lighthouse", [0, 1, 0], null),
			new("#12", "Lamp room", [0, 1, 1], null),
			new("#13", "Shed", null, null),
		],
		[
			new("#30", "North", ["n"], "#10", "#11", null),
			new("#31", "South", ["s"], "#11", "#10", null),
			new("#32", "Up", ["u"], "#11", "#12", null),
			new("#33", "Road", [], "#10", "#20", City),
		],
		"#10", Truncated: false)
	{
		LevelNames = new Dictionary<int, string> { [1] = "Lantern deck" },
	};

	/// <summary>The city, with the harbour drawn on it as a region the sailor is inside.</summary>
	public static readonly MapDto CityMap = new(City, [400, 300], [new("#20", "Square", [80, 80], null)], [], null, Truncated: false)
	{
		Regions = [new("#50", "Harbour", [250, 150], [120, 80])],
		HereRegion = "#50",
	};

	/// <summary>Caves under the harbour that open on the sewers, a visitor's level, below the sailor's.</summary>
	public static readonly MapDto CavesMap = new(Caves, null,
		[new("#40", "Drain", [0, 0, -1], null), new("#41", "Grotto", [0, 0, 0], null)], [], null, Truncated: false)
	{
		Level = -1,
		LevelNames = new Dictionary<int, string> { [-1] = "Sewers" },
	};

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
		Task.FromResult(request.RequestUri!.AbsolutePath.TrimStart('/') switch
		{
			"api/maps" => Json(new[] { City, Harbour, Caves }),
			"api/maps/50" => Json(HarbourMap),
			"api/maps/51" => Json(CityMap),
			"api/maps/52" => Json(CavesMap),
			_ => new HttpResponseMessage(HttpStatusCode.NotFound),
		});

	private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

public class WorldMapPageTests : TrackingBunitContext
{
	private void Arrange(bool acting = true)
	{
		var api = Track(new HttpClient(new WorldMapApiHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(api);
		var auth = Substitute.For<IAccountAuthState>();
		auth.ActiveCharacter.Returns(acting ? new AccountAuthService.CharacterSummary(5, 100, "Sailor", "") : null);
		var serverInfo = new StubServerInfoService(false, features: [GameFeatures.WorldMap]);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(auth)
			.AddSingleton<ServerInfoService>(serverInfo)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(sp => new WorldMapService(sp.GetRequiredService<IHttpClientFactory>(), serverInfo));
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task TheMapListNestsAMapUnderItsParent()
	{
		Arrange();

		var cut = Render<SharpMUSH.Client.Pages.WorldMap>();

		cut.WaitForAssertion(() => cut.Find(".map-tree .map-tree .map-tree-link"));
		await Assert.That(cut.Find(".map-tree .map-tree .map-tree-link").TextContent).IsEqualTo("Harbour");
		await Assert.That(cut.Find(".map-tree .map-tree .map-tree-link").GetAttribute("href")).IsEqualTo("/map/50");
	}

	[Test]
	public async Task AMapDrawsTheLevelYouStandOnWithYourRoomSelected()
	{
		Arrange();

		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));

		cut.WaitForAssertion(() => cut.Find(".map-room--here"));
		var names = cut.FindAll(".map-room").Select(room => room.TextContent.Trim()).ToList();
		await Assert.That(names).IsEquivalentTo(["Quay", "Lighthouse", "Shed"])
			.Because("the lamp room is a level up, and the shed has no place but is still drawn in the strip");
		await Assert.That(cut.Find(".map-room--here").TextContent.Trim()).IsEqualTo("Quay");
		await Assert.That(cut.Find(".map-room-panel").TextContent).Contains("NavMapToMap(City)");
	}

	[Test]
	public async Task ChoosingAnExitsRoomSelectsIt()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find("button.map-exit-to"));

		await cut.Find("button.map-exit-to").ClickAsync();

		await Assert.That(cut.Find(".map-room--selected").TextContent.Trim()).IsEqualTo("Lighthouse");
	}

	[Test]
	public async Task ChangingLevelShowsTheRoomsUpThere()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));

		var level = cut.FindAll("button[role=radio]").First(button => button.TextContent == "Lantern deck");
		await level.ClickAsync();

		await Assert.That(cut.FindAll(".map-room").Select(room => room.TextContent.Trim())).Contains("Lamp room");
		await Assert.That(cut.FindAll("button[role=radio]").Select(button => button.TextContent))
			.IsEquivalentTo(["NavMapLevel(0)", "Lantern deck"]).Because("a level without a name is numbered");
	}

	[Test]
	public async Task AVisitorStartsOnTheMapsOwnLevelAndStepsUp()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 52));
		cut.WaitForAssertion(() => cut.Find(".map-room"));

		await Assert.That(cut.FindAll(".map-room").Select(room => room.TextContent.Trim())).IsEquivalentTo(["Drain"]);
		await Assert.That(cut.Find("[aria-label=NavMapLevelDown]").HasAttribute("disabled")).IsTrue();

		await cut.Find("[aria-label=NavMapLevelUp]").ClickAsync();

		await Assert.That(cut.FindAll(".map-room").Select(room => room.TextContent.Trim())).IsEquivalentTo(["Grotto"]);
		await Assert.That(cut.Find("[aria-label=NavMapLevelUp]").HasAttribute("disabled")).IsTrue();
	}

	[Test]
	public async Task TheTrailNamesTheRegionsAMapIsInside()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 52));
		cut.WaitForAssertion(() => cut.Find(".map-trail"));

		await Assert.That(cut.FindAll(".map-trail a").Select(link => (link.TextContent, link.GetAttribute("href"))))
			.IsEquivalentTo([("City", "/map/51"), ("Harbour", "/map/50")]);
		await Assert.That(cut.Find(".map-trail [aria-current=page]").TextContent).IsEqualTo("Caves");
	}

	[Test]
	public async Task ARegionIsDrawnOnItsParentAndOpensIt()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 51));
		cut.WaitForAssertion(() => cut.Find("a.map-room--region"));

		var region = cut.Find("a.map-room--region");
		await Assert.That(region.GetAttribute("href")).IsEqualTo("/map/50");
		await Assert.That(region.ClassList).Contains("map-room--here").Because("the sailor stands in a room inside the harbour");
		await Assert.That(region.GetAttribute("aria-label")).IsEqualTo("NavMapRegion(Harbour), NavMapYouAreHere");
		await Assert.That(cut.FindAll(".map-trail")).IsEmpty().Because("the city is inside no other map");
	}

	[Test]
	public async Task WithNoCharacterThePageAsksForOne()
	{
		Arrange(acting: false);

		var cut = Render<SharpMUSH.Client.Pages.WorldMap>();

		cut.WaitForAssertion(() => cut.Find(".map-note"));
		await Assert.That(cut.Markup).Contains("NavMapNeedsCharacter");
		await Assert.That(cut.FindAll(".map-tree")).IsEmpty();
	}
}

public class MapLayoutTests
{
	private static MapDto Map(string kind, MapRoomDto[] rooms, MapExitDto[] exits, string? here = null) =>
		new(new("#50", "Test", kind, null), null, rooms, exits, here, false);

	private static MapExitDto Exit(int id, string name, string from, string to) => new($"#{id}", name, [], from, to, null);

	[Test]
	public async Task ACoordinateMapPutsNorthUp()
	{
		var layout = new MapLayout(Map(MapKinds.Coordinates, [new("#1", "South", [0, 0, 0], null), new("#2", "North", [0, 1, 0], null)], []));

		await Assert.That(layout.Rooms["#2"].Y).IsLessThan(layout.Rooms["#1"].Y);
		await Assert.That(layout.Rooms["#2"].X).IsEqualTo(layout.Rooms["#1"].X);
	}

	[Test]
	public async Task ARoomSizeCoversMoreCellsNorthAndEastOfItsCorner()
	{
		var layout = new MapLayout(Map(MapKinds.Coordinates, [new("#1", "Hall", [0, 0, 0], [2, 3]), new("#2", "Cell", [0, 0, 0], null)], []));

		await Assert.That(layout.Rooms["#1"].Width).IsEqualTo(2 * MapLayout.Cell);
		await Assert.That(layout.Rooms["#1"].Height).IsEqualTo(3 * MapLayout.Cell);
		await Assert.That(layout.Rooms["#1"].Y + layout.Rooms["#1"].Height).IsEqualTo(layout.Rooms["#2"].Y + layout.Rooms["#2"].Height)
			.Because("both stand on the same south-west cell");
	}

	[Test]
	public async Task ACompassMapPlacesRoomsByTheirExits()
	{
		var layout = new MapLayout(Map(MapKinds.Compass,
			[new("#1", "Gate", null, null), new("#2", "Road", null, null), new("#3", "Tower top", null, null), new("#4", "Well", null, null)],
			[Exit(10, "North", "#1", "#2"), new("#11", "Climb", ["u"], "#2", "#3", null), Exit(12, "West", "#4", "#1")], here: "#1"));

		await Assert.That(layout.Rooms["#2"].Y).IsLessThan(layout.Rooms["#1"].Y);
		await Assert.That(layout.Rooms["#3"].Level).IsEqualTo(1);
		await Assert.That(layout.Rooms["#4"].X).IsGreaterThan(layout.Rooms["#1"].X)
			.Because("west from the well reaches the gate, so the well is east of it");
		await Assert.That(layout.Rooms.Values.All(room => room.Placed)).IsTrue();
	}

	[Test]
	public async Task ACompassMapKeepsAStoredCell()
	{
		var layout = new MapLayout(Map(MapKinds.Compass,
			[new("#1", "Gate", [5, 5, 0], null), new("#2", "Road", null, null)], [Exit(10, "n", "#1", "#2")]));

		await Assert.That(layout.Rooms["#1"].X).IsEqualTo(5 * MapLayout.Cell);
		await Assert.That(layout.Rooms["#2"].X).IsEqualTo(5 * MapLayout.Cell);
		await Assert.That(layout.Rooms["#2"].Y).IsEqualTo(layout.Rooms["#1"].Y - MapLayout.Cell);
	}

	[Test]
	public async Task AnUnconnectedCompassWingIsDrawnApart()
	{
		var layout = new MapLayout(Map(MapKinds.Compass,
			[new("#1", "A", null, null), new("#2", "B", null, null), new("#3", "C", null, null)], [Exit(10, "east", "#1", "#2")]));

		var cells = layout.Rooms.Values.Select(room => (room.X, room.Y, room.Level)).ToList();
		await Assert.That(cells.Distinct().Count()).IsEqualTo(3);
	}

	[Test]
	public async Task AGraphMapCentresARoomOnItsPoint()
	{
		var layout = new MapLayout(Map(MapKinds.Graph, [new("#1", "Square", [200, 100], null), new("#2", "Nowhere", null, null)], []));

		await Assert.That(layout.Rooms["#1"].X + layout.Rooms["#1"].Width / 2).IsEqualTo(200);
		await Assert.That(layout.Rooms["#2"].Placed).IsFalse();
		await Assert.That(layout.Unplaced(0).Single().Room.Id).IsEqualTo("#2");
	}

	[Test]
	public async Task AnUnplacedRoomIsDrawnOnTheLevelItsExitsReach()
	{
		var layout = new MapLayout(Map(MapKinds.Coordinates,
			[new("#1", "Hall", [0, 0, 0], null), new("#2", "Attic", [0, 0, 1], null), new("#3", "Crawlspace", null, null), new("#4", "Shed", null, null)],
			[Exit(10, "Hatch", "#2", "#3")]));

		await Assert.That(layout.Unplaced(1).Select(room => room.Room.Id)).IsEquivalentTo(["#3"]);
		await Assert.That(layout.Unplaced(0).Select(room => room.Room.Id)).IsEquivalentTo(["#4"])
			.Because("a room no exit joins to the map is drawn on the ground level");
	}

	[Test]
	public async Task ExitNamesAndAliasesGiveTheirDirection()
	{
		await Assert.That(MapLayout.Direction(new("#1", "Out", ["o"], "#1", "#2", null))).IsNull();
		await Assert.That(MapLayout.Direction(new("#1", "Ladder", ["U"], "#1", "#2", null))).IsEqualTo((0, 0, 1));
		await Assert.That(MapLayout.Direction(new("#1", "Southwest", [], "#1", "#2", null))).IsEqualTo((-1, -1, 0));
	}

	[Test]
	public async Task ARegionIsPlacedAsARoomIs()
	{
		var map = Map(MapKinds.Coordinates, [new("#1", "Quay", [0, 0, 0], null)], []);
		var layout = new MapLayout(map with
		{
			Regions = [new("#60", "Old Town", [2, 0, 0], [2, 2]), new("#61", "Docks", null, null)],
		});

		await Assert.That(layout.Rooms["#60"].Region).IsTrue();
		await Assert.That(layout.Rooms["#60"].X).IsEqualTo(2 * MapLayout.Cell);
		await Assert.That(layout.Rooms["#60"].Width).IsEqualTo(2 * MapLayout.Cell);
		await Assert.That(layout.Rooms["#61"].Placed).IsFalse();
		await Assert.That(layout.Unplaced(0).Select(room => room.Room.Name)).IsEquivalentTo(["Docks"]);
	}
}
