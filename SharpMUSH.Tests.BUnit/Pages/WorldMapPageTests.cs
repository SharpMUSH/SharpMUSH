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
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>The slice of <c>api/maps</c> the /map page reads: a harbour with a door to the city.</summary>
internal sealed class WorldMapApiHandler : HttpMessageHandler
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

	/// <summary>An island drawn on a picture: one pin, one room with a size of its own.</summary>
	public static readonly MapDto IsleMap = new(new("#53", "Isle", MapKinds.Graph, null), [900, 640],
		[new("#60", "Ford", [470, 390], null), new("#61", "Saltpans", [600, 520], [120, 80])], [], null, Truncated: false)
	{
		Backdrop = "/api/wiki-assets/7/isle.webp",
	};

	/// <summary>The same island with a picture from an http address, which the portal never loads.</summary>
	public static readonly MapDto PlainIsleMap = IsleMap with { Map = new("#54", "Plain isle", MapKinds.Graph, null), Backdrop = "http://example.com/isle.png" };

	/// <summary>The harbour as a builder who controls it and its rooms sees it.</summary>
	public static readonly MapDto WorksMap = HarbourMap with
	{
		Map = new("#55", "Harbour works", MapKinds.Coordinates, null),
		Rooms = [.. HarbourMap.Rooms.Select(room => room with { Editable = true })],
		CanEdit = true,
	};

	/// <summary>The way up to the lighthouse: one step, from the quay.</summary>
	public static readonly MapRouteDto ToLighthouse = new([new("#30", "North", "#10", "#11", "Lighthouse", Harbour)], "walk North");

	/// <summary>The lines run through <c>POST api/commands</c>, in order.</summary>
	public List<string> Commands { get; } = [];

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (path == "api/commands" && request.Content is not null)
		{
			var sent = await request.Content.ReadFromJsonAsync<PortalCommandRequest>(cancellationToken);
			Commands.Add(sent!.Command);
			return Json(new PortalCommandResponse([], Read(sent), false));
		}
		return path switch
		{
			"api/maps" => Json(new[] { City, Harbour, Caves }),
			"api/maps/50" => Json(HarbourMap),
			"api/maps/51" => Json(CityMap),
			"api/maps/52" => Json(CavesMap),
			"api/maps/53" => Json(IsleMap),
			"api/maps/54" => Json(PlainIsleMap),
			"api/maps/55" => Json(WorksMap),
			"api/maps/route/11" => Json(ToLighthouse),
			"api/maps/route/10" => Json(new MapRouteDto([], null)),
			_ => new HttpResponseMessage(HttpStatusCode.NotFound),
		};
	}

	/// <summary>What a command's check reads in a game where every command works: <c>@dig</c> makes #90.</summary>
	private static string Read(PortalCommandRequest sent) => sent.Result switch
	{
		"%>" when sent.Command.StartsWith("@dig", StringComparison.Ordinal) => "#90:1793148715366",
		"%>" => "#91:1793148715367",
		{ } check when check.StartsWith("zone(", StringComparison.Ordinal) => "#55:1793148715000",
		{ } check when check.StartsWith("get(", StringComparison.Ordinal) => sent.Command[(sent.Command.IndexOf('=') + 1)..],
		_ => "#11",
	};

	private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

public class WorldMapPageTests : TrackingBunitContext
{
	private readonly WorldMapApiHandler _api = new();

	private void Arrange(bool acting = true)
	{
		var api = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
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
			.AddSingleton(sp => new WorldMapService(sp.GetRequiredService<IHttpClientFactory>(), serverInfo))
			.AddSingleton(sp => new GameCommandService(sp.GetRequiredService<IHttpClientFactory>()));
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

	[Test]
	public async Task ChoosingARoomShowsTheWayThereAsOneWalkLine()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find("button.map-exit-to"));

		await cut.Find("button.map-exit-to").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".map-route-command code"));
		await Assert.That(cut.Find(".map-route-command code").TextContent).IsEqualTo("walk North");
		await Assert.That(cut.Find(".map-route-steps").TextContent).Contains("NavMapRouteStep(North, Lighthouse)");
		await Assert.That(cut.FindAll(".map-edge--route")).Count().IsEqualTo(1);
	}

	[Test]
	public async Task WalkThereRunsTheLineAsTheCharacter()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find("button.map-exit-to"));
		await cut.Find("button.map-exit-to").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".map-route button.mud-button-filled"));

		await cut.Find(".map-route button.mud-button-filled").ClickAsync();

		cut.WaitForState(() => _api.Commands.Count > 0);
		await Assert.That(_api.Commands).IsEquivalentTo(["walk North"]);
		await Assert.That(cut.FindAll(".map-route-error")).IsEmpty();
	}

	[Test]
	public async Task APictureMapDrawsItsBackdropUnderPins()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 53));
		cut.WaitForAssertion(() => cut.Find(".map-room"));

		await Assert.That(cut.Find("image.map-backdrop").GetAttribute("href")).IsEqualTo("/api/wiki-assets/7/isle.webp");
		await Assert.That(cut.FindAll(".map-room--pin").Select(room => room.TextContent.Trim())).IsEquivalentTo(["Ford"])
			.Because("Saltpans has a size of its own, so it is drawn as an area");
	}

	[Test]
	public async Task OnlyABuilderWhoControlsTheMapMayEditIt()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));

		await Assert.That(cut.Markup).DoesNotContain("NavMapEdit");
		await Assert.That(cut.FindAll(".map-cell")).IsEmpty();
	}

	[Test]
	public async Task DiggingAnEmptyCellRunsTheBuildersCommandsOnApply()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 55));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));

		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.Find("button.map-cell[aria-label='NavMapCell(1, 0)']").ClickAsync();
		await cut.Find(".map-builder input").InputAsync("Boathouse");
		await Assert.That(_api.Commands).IsEmpty().Because("nothing reaches the game before Apply");
		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapAddToPlan").ClickAsync();

		await Assert.That(cut.FindAll(".map-room").Select(room => room.TextContent.Trim())).Contains("Boathouse")
			.Because("the map shows the plan before it is applied");

		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapReview").ClickAsync();
		await Assert.That(cut.Find(".map-review-commands").TextContent).Contains("@open East;e=<Boathouse>,West;w,#10");
		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapApply(1)").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".map-applied"));
		await Assert.That(_api.Commands).IsEquivalentTo(
		[
			"@dig Boathouse",
			"@chzone #90:1793148715366=#55",
			"&MAP`COORDS`X #90:1793148715366=1",
			"&MAP`COORDS`Y #90:1793148715366=0",
			"@open East;e=#90:1793148715366,West;w,#10",
		]);
		await Assert.That(cut.Find(".map-applied-list li").ClassList).Contains("map-applied--done");
	}

	[Test]
	public async Task MovingARoomShowsItWhereItWillBe()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 55));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.FindAll("button.map-room").First(room => room.TextContent.Trim() == "Lighthouse").ClickAsync();
		await Assert.That(cut.Find(".map-builder input").GetAttribute("value")).IsEqualTo("0");

		await cut.Find("button.map-cell[aria-label='NavMapCell(1, 1)']").ClickAsync();
		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapMoveHere(Lighthouse)").ClickAsync();

		await Assert.That(cut.Find(".map-builder input").GetAttribute("value")).IsEqualTo("1").Because("the fields follow the room");
		await Assert.That(cut.Find(".map-builder").TextContent).Contains("NavMapChangesWaiting(1)");
		await Assert.That(_api.Commands).IsEmpty();
	}

	[Test]
	public async Task APictureFromAnHttpAddressIsNotLoaded()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 54));
		cut.WaitForAssertion(() => cut.Find(".map-room"));

		await Assert.That(cut.FindAll("image.map-backdrop")).IsEmpty();
		await Assert.That(cut.FindAll(".map-room--pin")).IsEmpty();
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
