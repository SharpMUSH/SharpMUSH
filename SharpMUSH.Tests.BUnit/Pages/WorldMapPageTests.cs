using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Map;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Services.Interfaces;
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
		Exits = [.. HarbourMap.Exits.Select(exit => exit with { Editable = exit.Id != "#32" })],
		CanEdit = true,
	};

	/// <summary>Two rooms a long way apart, which a builder controls: edit mode offers only the cells beside each.</summary>
	public static readonly MapDto FarMap = new(new("#56", "Far reaches", MapKinds.Coordinates, null), null,
		[new("#70", "Near", [0, 0, 0], null) { Editable = true }, new("#71", "Far", [10000, 10000, 0], null) { Editable = true }], [], "#70",
		Truncated: false)
	{
		CanEdit = true,
	};

	/// <summary>A shore whose legend draws water blue with an icon and sand with a glyph, and names a kind no room is.</summary>
	public static readonly MapDto ShoreMap = new(new("#57", "Shore", MapKinds.Coordinates, null), null,
		[
			new("#80", "Shallows", [0, 0, 0], null) { Environment = "water" },
			new("#81", "Beach", [1, 0, 0], null) { Environment = "sand" },
			new("#82", "Hut", [2, 0, 0], null),
		], [], null, Truncated: false)
	{
		Legend =
		[
			new("sand", "Sand", null, "∴"), new("water", "Open water", "blue", "water"), new("forest", "Forest", "green", "forest"),
		],
	};

	/// <summary>The way up to the lighthouse: one step, from the quay.</summary>
	public static readonly MapRouteDto ToLighthouse = new([new("#30", "North", "#10", "#11", "Lighthouse", Harbour)], "walk North");

	/// <summary>The lines run through <c>POST api/commands</c>, in order.</summary>
	public List<string> Commands { get; } = [];

	/// <summary>What <c>api/maps/here</c> answers; null answers 404.</summary>
	public MapHereDto? Here { get; set; }

	/// <summary>A pier with two sailors on it, and a ladder down to a room on no map.</summary>
	public static readonly MapDto PierMap = new(new("#58", "Pier", MapKinds.Coordinates, null), null,
		[new("#83", "Pier head", [0, 0, 0], null) { People = ["Ada", "Bram"] }],
		[new("#34", "Ladder", [], "#83", "#84", null) { DestinationName = "Boat" }], null, Truncated: false);

	/// <summary>Wilds whose places a map source stores, 41 cells a side over two levels, with one room standing at the middle.</summary>
	public static readonly MapDto WildsMap = new(new("#59", "Wilds", MapKinds.Coordinates, null), null,
		[new("#85", "Camp", [0, 0, 0], null)], [], "#85", Truncated: false)
	{
		Space = new(MapSpaces.Square, -20, -20, 20, 20, [0, 1]),
		Legend = [new("forest", "Forest", "green", "forest"), new("water", "Deep water", "blue", "~")],
	};

	/// <summary>The wilds again as a hex map, the steppe.</summary>
	public static readonly MapDto SteppeMap = WildsMap with { Map = new("#62", "Steppe", MapKinds.Coordinates, null), Space = new(MapSpaces.Hex, -20, -20, 20, 20, [0]) };

	/// <summary>The wilds again as a pointy hex map, the fen: the camp and a pool east of it, nothing walled.</summary>
	public static readonly MapDto FenMap = WildsMap with { Map = new("#63", "Fen", MapKinds.Coordinates, null), Space = new(MapSpaces.PointyHex, -20, -20, 20, 20, [0]) };

	/// <summary>
	/// The steppe's places: the camp, and the ford north-east of it. The camp's north-east side is a wall, its south-east
	/// side locked, its north side one way out, and its north-west side a wall, listed as the south-east side of the place
	/// it shares it with.
	/// </summary>
	public static MapCellsDto SteppeCells(int left, int bottom, int right, int top) => new(0, left, bottom, right, top,
		[new(0, 0, "forest") { Room = "#85" }, new(1, 0, "water") { Name = "Ford" }, new(-1, 1, "forest")],
		[new(0, 0, "ne", MapWallKinds.Wall, "Reeds."), new(0, 0, "se", MapWallKinds.Lock, "A gate."), new(0, 0, "n", MapWallKinds.OneWay, null),
			new(-1, 1, "se", MapWallKinds.Wall, null)],
		false);

	/// <summary>The windows of the wilds' places read, as their queries.</summary>
	public List<string> CellsAsked { get; } = [];

	/// <summary>When set, a read of the wilds' level 1 waits for it.</summary>
	public TaskCompletionSource? LevelOneHeld { get; set; }

	/// <summary>What a window of the wilds holds on <paramref name="level"/>.</summary>
	public static MapCellsDto WildsCells(int level, int left, int bottom, int right, int top) => level == 0
		? new(0, left, bottom, right, top,
			[
				new(0, 0, "forest") { Room = "#85" },
				new(1, 0, "water") { Closed = true, Reason = "Too deep to wade." },
				new(0, 1, "forest")
				{
					Name = "Old Mill", Image = "/api/wiki-assets/7/mill.webp", People = 1,
					Info = [new("Owner", "Miller Bram"), new("Grinds", "Barley")],
				},
			],
			[new(0, 0, "n", MapWallKinds.Wall, null), new(0, 0, "e", MapWallKinds.Lock, "The gate is barred."), new(1, 0, "w", MapWallKinds.OneWay, null),
				new(0, 1, "d", MapWallKinds.Lock, "A trapdoor.")],
			false)
		: new(1, left, bottom, right, top, [new(0, 0, "forest") { Name = "Lookout" }], [], false);

	/// <summary>Whether the viewer controls the wilds, so may plan changes to its places.</summary>
	public bool WildsEditable { get; set; }

	/// <summary>How many more objects the builder of the harbour works may build; null for no quota.</summary>
	public int? BuildsLeft { get; set; }

	/// <summary>What an <c>@open</c>'s check reads: 1 when both exits were opened and linked.</summary>
	public string OpenReads { get; set; } = "1";

	/// <summary>What <c>api/maps/50</c> answers in place of the harbour, once the sailor walked.</summary>
	public MapDto? HarbourNow { get; set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (path == "api/commands/eval" && request.Content is not null)
		{
			var asked = await request.Content.ReadFromJsonAsync<PortalEvalRequest>(cancellationToken);
			return Json(new PortalCommandResponse([], asked!.Expression switch
			{
				"u(#83/SPIRIT`DESC)" => "Planks, tar\nand gulls.",
				"get(#11/DESCRIBE)" => "Whitewashed.%rA lamp above.",
				_ => "",
			}, false));
		}
		if (path == "api/commands" && request.Content is not null)
		{
			var sent = await request.Content.ReadFromJsonAsync<PortalCommandRequest>(cancellationToken);
			Commands.Add(sent!.Command);
			return Json(new PortalCommandResponse([], Read(sent), false));
		}
		if (path == "api/maps/62/cells")
		{
			var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
			return Json(SteppeCells(int.Parse(query["left"]!), int.Parse(query["bottom"]!), int.Parse(query["right"]!), int.Parse(query["top"]!)));
		}
		if (path == "api/maps/63/cells")
		{
			var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
			return Json(new MapCellsDto(0, int.Parse(query["left"]!), int.Parse(query["bottom"]!), int.Parse(query["right"]!), int.Parse(query["top"]!),
				[new(0, 0, "forest") { Room = "#85" }, new(1, 0, "water") { Name = "Pool" }], [], false));
		}
		if (path == "api/maps/59/cells")
		{
			CellsAsked.Add(request.RequestUri.Query);
			var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
			if (query["level"] == "1" && LevelOneHeld is { } held) await held.Task;
			return Json(WildsCells(int.Parse(query["level"]!), int.Parse(query["left"]!), int.Parse(query["bottom"]!),
				int.Parse(query["right"]!), int.Parse(query["top"]!)));
		}
		return path switch
		{
			"api/maps" => Json(new[] { City, Harbour, Caves }),
			"api/maps/50" => Json(HarbourNow ?? HarbourMap),
			"api/maps/51" => Json(CityMap),
			"api/maps/52" => Json(CavesMap),
			"api/maps/53" => Json(IsleMap),
			"api/maps/54" => Json(PlainIsleMap),
			"api/maps/55" => Json(WorksMap with { BuildsLeft = BuildsLeft }),
			"api/maps/56" => Json(FarMap),
			"api/maps/57" => Json(ShoreMap),
			"api/maps/58" => Json(PierMap),
			"api/maps/59" => Json(WildsMap with { CanEdit = WildsEditable }),
			"api/maps/62" => Json(SteppeMap with { CanEdit = WildsEditable }),
			"api/maps/63" => Json(FenMap with { CanEdit = WildsEditable }),
			"api/maps/here" when Here is { } here => Json(here),
			"api/maps/route/11" => Json(ToLighthouse),
			"api/maps/route/10" => Json(new MapRouteDto([], null)),
			// The pier's sailors see it as the spirit layer describes it.
			"api/maps/description/83" => Json(new MapDescriptionDto("SPIRIT`DESC")),
			_ => new HttpResponseMessage(HttpStatusCode.NotFound),
		};
	}

	/// <summary>What a command's check reads in a game where every command works: <c>@dig</c> makes #90.</summary>
	private string Read(PortalCommandRequest sent) => sent.Result switch
	{
		"%>" when sent.Command.StartsWith("@dig", StringComparison.Ordinal) => "#90:1793148715366",
		"%>" => "#91:1793148715367",
		_ when sent.Command.StartsWith("@open", StringComparison.Ordinal) => OpenReads,
		{ } check when check.StartsWith("zone(", StringComparison.Ordinal) => "#55:1793148715000",
		{ } check when check.StartsWith("get(", StringComparison.Ordinal) => sent.Command[(sent.Command.IndexOf('=') + 1)..],
		_ => "#11",
	};

	private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

public class WorldMapPageTests : TrackingBunitContext
{
	private readonly WorldMapApiHandler _api = new();

	private readonly IAccountAuthState _auth = Substitute.For<IAccountAuthState>();

	private readonly IConnectionStateService _hub = Substitute.For<IConnectionStateService>();

	private void Arrange(bool acting = true)
	{
		var api = Track(new HttpClient(_api) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(api);
		var auth = _auth;
		auth.ActiveCharacter.Returns(acting ? new AccountAuthService.CharacterSummary(5, 100, "Sailor", "") : null);
		var serverInfo = new StubServerInfoService(false, features: [GameFeatures.WorldMap]);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(auth)
			.AddSingleton(_hub)
			.AddSingleton<ServerInfoService>(serverInfo)
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(sp => new WorldMapService(sp.GetRequiredService<IHttpClientFactory>(), serverInfo))
			.AddSingleton(sp => new GameCommandService(sp.GetRequiredService<IHttpClientFactory>()))
			.AddLogging()
			.AddSingleton<WikiAssetService>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task ARoomIsDrawnInItsKindsToneWithItsIconAndTheLegendNamesTheKindsOnShow()
	{
		Arrange();

		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 57));
		cut.WaitForAssertion(() => cut.Find(".map-legend"));

		await Assert.That(cut.FindAll("rect.map-box--toned").Select(box => box.GetAttribute("style")))
			.IsEquivalentTo(["--room-tone: var(--hue-blue);"]).Because("sand has no tone and the hut has no kind");
		var shallows = cut.FindAll(".map-room").Single(room => room.TextContent.Contains("Shallows"));
		await Assert.That(shallows.QuerySelector(".map-room-icon")).IsNotNull();
		await Assert.That(shallows.GetAttribute("aria-label")).IsEqualTo("Shallows, Open water");
		await Assert.That(cut.FindAll(".map-room").Single(room => room.TextContent.Contains("Beach")).TextContent).Contains("∴");
		var kinds = cut.FindAll(".map-legend li").Select(kind => kind.TextContent).ToList();
		await Assert.That(kinds).Count().IsEqualTo(2).Because("no room on the shore is forest");
		await Assert.That(kinds[0]).Contains("Sand");
		await Assert.That(kinds[1]).Contains("Open water");
	}

	[Test]
	public async Task WithNoMapChosenTheMapYouAreOnOpens()
	{
		_api.Here = new("#10", "Quay", WorldMapApiHandler.Harbour);
		Arrange();

		Render<SharpMUSH.Client.Pages.WorldMap>();

		var nav = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
		await Assert.That(nav.Uri).EndsWith("/map/50");
	}

	[Test]
	public async Task WithNoMapChosenSomeoneOnNoMapIsToldWhereTheyAre()
	{
		_api.Here = new("#0", "Room Zero", null);
		Arrange();

		var cut = Render<SharpMUSH.Client.Pages.WorldMap>();

		cut.WaitForAssertion(() => cut.Find(".map-main .map-note"));
		await Assert.That(cut.Find(".map-main .map-note").TextContent).Contains("NavMapHereOffMap(Room Zero)");
	}

	[Test]
	public async Task ARoomShowsItsDescriptionWhoIsThereAndWhereAnExitOffTheMapLeads()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 58));
		cut.WaitForAssertion(() => cut.Find(".map-room"));

		await cut.Find(".map-room").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".map-room-desc"));
		await Assert.That(cut.Find(".map-room-desc").TextContent).IsEqualTo("Planks, tar\nand gulls.");
		await Assert.That(cut.Find(".map-room-people").TextContent).IsEqualTo("NavMapPeopleHere(Ada, Bram)");
		await Assert.That(cut.Find(".map-room-count").TextContent.Trim()).IsEqualTo("2");
		await Assert.That(cut.Find(".map-room").GetAttribute("aria-label")).IsEqualTo("Pier head, NavMapPeopleCount(2)");
		await Assert.That(cut.Find(".map-exit-to--off").TextContent).IsEqualTo("NavMapOffMap(Boat)");
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
	public async Task TheMapFollowsTheSailorUpTheLighthouse()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));

		_api.HarbourNow = WorldMapApiHandler.HarbourMap with { Here = "#11" };
		_api.Here = new("#11", "Lighthouse", WorldMapApiHandler.Harbour);
		_hub.OnCharacterMoved += Raise.Event<Action<CharacterMovedMessage>>(new CharacterMovedMessage("#11:1"));

		cut.WaitForState(() => cut.Find(".map-room--here").TextContent.Trim() == "Lighthouse");
		await Assert.That(cut.Find(".map-room--selected").TextContent.Trim()).IsEqualTo("Lighthouse")
			.Because("the sailor's own room was chosen, so the choice follows them");
		await Assert.That(JSInterop.Invocations["SharpMUSH.mapCentre"].Count).IsGreaterThan(0);
	}

	[Test]
	public async Task TheMapFollowsTheSailorOntoAnotherMap()
	{
		Arrange();
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));

		_api.Here = new("#20", "Square", WorldMapApiHandler.City);
		_hub.OnCharacterMoved += Raise.Event<Action<CharacterMovedMessage>>(new CharacterMovedMessage("#20:1"));

		cut.WaitForState(() => nav.Uri.EndsWith("/map/51", StringComparison.Ordinal));
		await Assert.That(nav.Uri).EndsWith("/map/51");
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
	public async Task AStoredMapDrawsThePlacesAroundYouInTheirTerrainWithClosedOnesHatchedAndItsWalls()
	{
		Arrange();

		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 59));
		cut.WaitForAssertion(() => cut.Find("rect.map-place"));

		await Assert.That(_api.CellsAsked).IsEquivalentTo(["?level=0&left=-16&bottom=-16&right=16&top=16"])
			.Because("the view starts on the camp, eight cells around it, and reads half as much again on each side");
		await Assert.That(cut.FindAll("rect.map-place").Select(place => place.GetAttribute("style")))
			.IsEquivalentTo(["--room-tone: var(--hue-green);", "--room-tone: var(--hue-blue);", "--room-tone: var(--hue-green);"]);
		await Assert.That(cut.FindAll("rect.map-place-hatch")).Count().IsEqualTo(1);
		await Assert.That(cut.FindAll("text.map-place-glyph").Select(glyph => glyph.TextContent)).IsEquivalentTo(["~"])
			.Because("a named icon is drawn on a button, and only a place with something to say is one");
		await Assert.That(cut.FindAll("line.map-wall").Count).IsEqualTo(2);
		await Assert.That(cut.FindAll("line.map-wall--lock").Count).IsEqualTo(1);
		await Assert.That(cut.Find("line.map-oneway").GetAttribute("marker-end")).StartsWith("url(#map-arrow-");
		await Assert.That(cut.FindAll(".map-room").Select(room => room.TextContent.Trim())).IsEquivalentTo(["Old Mill1", "Camp"])
			.Because("the camp's place is drawn by its room");
		var kinds = cut.FindAll(".map-legend li").Select(kind => kind.TextContent).ToList();
		await Assert.That(kinds).Count().IsEqualTo(2);
		await Assert.That(kinds[0]).Contains("Forest");
		await Assert.That(kinds[1]).Contains("Deep water");
	}

	[Test]
	public async Task ANamedPlaceOpensAPanelOfWhatItsDataSays()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 59));
		cut.WaitForAssertion(() => cut.Find(".map-room--place"));
		var mill = cut.Find(".map-room--place");
		await Assert.That(mill.GetAttribute("aria-label")).IsEqualTo("Old Mill, Forest, NavMapPeopleCount(1)");

		await mill.ClickAsync();

		var panel = cut.Find(".map-room-panel");
		await Assert.That(panel.TextContent).Contains("Old Mill");
		await Assert.That(cut.FindAll(".map-place-info dt").Select(label => label.TextContent)).IsEquivalentTo(["Owner", "Grinds"]);
		await Assert.That(cut.FindAll(".map-place-info dd").Select(value => value.TextContent)).IsEquivalentTo(["Miller Bram", "Barley"]);
		await Assert.That(cut.Find(".map-place-image").GetAttribute("src")).EndsWith("/api/wiki-assets/7/mill.webp");
		await Assert.That(cut.Find(".map-room--place").GetAttribute("aria-pressed")).IsEqualTo("true");
	}

	[Test]
	public async Task MovingTheViewReadsPlacesOnlyWhenItLeavesThoseRead()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 59));
		cut.WaitForAssertion(() => cut.Find("rect.map-place"));
		var canvas = cut.FindComponent<MapCanvas>();

		await cut.InvokeAsync(() => canvas.Instance.ViewAt(1_000_000, 1_000_000, 10, 10));
		await Assert.That(_api.CellsAsked).Count().IsEqualTo(1).Because("past the stored places there is nothing to read");

		// The drawing starts a margin of 24 pixels west and north of cell (-20, 20), so cell (0, 0) is 1944 pixels in.
		await cut.InvokeAsync(() => canvas.Instance.ViewAt(1944, 1944, 96, 96));
		await Assert.That(_api.CellsAsked).Count().IsEqualTo(1).Because("the camp's cell is inside what was read");

		await cut.InvokeAsync(() => canvas.Instance.ViewAt(1944 + 18 * 96, 1944, 96, 96));
		await Assert.That(_api.CellsAsked[^1]).IsEqualTo("?level=0&left=17&bottom=-1&right=20&top=2")
			.Because("eighteen cells east is past what was read; the read stops at the stored places' edge");

		await cut.Find("[aria-label=NavMapLevelUp]").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".map-room--place"));

		await Assert.That(_api.CellsAsked[^1]).StartsWith("?level=1&");
		await Assert.That(cut.Find(".map-room--place").TextContent.Trim()).IsEqualTo("Lookout");
	}

	[Test]
	public async Task GoingBackToALevelBeforeItsReadReturnsKeepsItsPlaces()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 59));
		cut.WaitForAssertion(() => cut.Find(".map-room--place"));
		_api.LevelOneHeld = new TaskCompletionSource();

		// The way up waits on its held read, which returns only once the way down has been taken.
		var up = cut.Find("[aria-label=NavMapLevelUp]").ClickAsync();
		await cut.Find("[aria-label=NavMapLevelDown]").ClickAsync();
		_api.LevelOneHeld.SetResult();
		await up;

		await Assert.That(cut.Find(".map-room--place").TextContent).Contains("Old Mill")
			.Because("the level-1 read returned after going back down, and is dropped");
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
	public async Task AnotherCharacterDoesNotSeeTheLastOnesRoute()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find("button.map-exit-to"));
		await cut.Find("button.map-exit-to").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".map-route-command code"));

		_auth.ActiveCharacter.Returns(new AccountAuthService.CharacterSummary(6, 200, "Diver", ""));
		_auth.ActiveCharacterChanged += Raise.Event<Action>();

		cut.WaitForAssertion(() => cut.Find(".map-room--here"));
		await Assert.That(cut.FindAll(".map-route-command")).IsEmpty()
			.Because("the way there was the last character's, from where they stood");
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
	public async Task ADigWhoseWayBackWasNotOpenedFails()
	{
		Arrange();
		_api.OpenReads = "0";
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 55));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.Find("button.map-cell[aria-label='NavMapCell(1, 0)']").ClickAsync();
		await cut.Find(".map-builder input").InputAsync("Boathouse");
		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapAddToPlan").ClickAsync();
		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapReview").ClickAsync();

		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapApply(1)").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".map-applied"));
		await Assert.That(cut.Find(".map-applied-list li").ClassList).Contains("map-applied--failed")
			.Because("@open names the exit it made even when the way back could not be opened");
	}

	[Test]
	public async Task TheMapsSettingsJoinThePlan()
	{
		var cut = await EditWorksAsync(authorized: true);

		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapSettingsShow").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".map-settings input"));
		await cut.Find(".map-settings input").InputAsync("Old works");
		await cut.FindAll(".map-settings button").First(button => button.TextContent.Trim() == "NavMapSetName").ClickAsync();
		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapReview").ClickAsync();

		await Assert.That(cut.Find(".map-review").TextContent).Contains("@name #55=Old works");
	}

	[Test]
	public async Task AMapNameThatNameWouldEvaluateIsRefused()
	{
		var cut = await EditWorksAsync(authorized: true);

		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapSettingsShow").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".map-settings input"));
		await cut.Find(".map-settings input").InputAsync("Harbour [West]");

		await Assert.That(cut.FindAll(".map-settings button").First(button => button.TextContent.Trim() == "NavMapSetName").HasAttribute("disabled")).IsTrue()
			.Because("@name evaluates its name, so [West] would be stored as something else");
		await Assert.That(cut.Find(".map-settings").TextContent).Contains("NavMapMapNameCharacter");
	}

	[Test]
	public async Task ABuilderNamesHidesAndPaintsAPlaceOfAWorldMap()
	{
		Arrange();
		_api.WildsEditable = true;
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 59));
		cut.WaitForAssertion(() => cut.Find(".map-room--place"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.Find(".map-room--place").ClickAsync();

		await Assert.That(cut.Find("#map-place-title").TextContent).IsEqualTo("NavMapPlaceAt(0, 1, 0)");
		await Assert.That(cut.Find(".map-builder input").GetAttribute("value")).IsEqualTo("Old Mill");
		await cut.Find(".map-builder input").InputAsync("Mill ruins");
		await Click(cut, "NavMapSetName");
		await Click(cut, "NavMapHidePlace");
		await Click(cut, "NavMapPaint");
		await Click(cut, "NavMapReview");

		await Assert.That(cut.FindAll(".map-review-commands").Select(commands => commands.TextContent)).IsEquivalentTo(
		[
			"@world/place #59/0,1,0=Mill ruins",
			"""@world/data/merge #59/0,1,0={"render":{"hidden":true}}""",
			"@world/paint #59/0,1,0=forest",
		]).Because("the paint box starts on the place chosen, with its terrain");
	}

	[Test]
	public async Task ThePlaceFieldsFollowTheLevelOnShow()
	{
		Arrange();
		_api.WildsEditable = true;
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 59));
		cut.WaitForAssertion(() => cut.Find(".map-room--place"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.Find(".map-room--place").ClickAsync();
		await Assert.That(cut.Find(".map-builder input").GetAttribute("value")).IsEqualTo("Old Mill");

		await cut.Find("[aria-label=NavMapLevelUp]").ClickAsync();

		cut.WaitForAssertion(() => cut.Find("#map-place-title").TextContent.Contains("NavMapPlaceAt(0, 1, 1)"));
		cut.WaitForAssertion(() => AssertPlaceNameEmpty(cut));
	}

	private static void AssertPlaceNameEmpty(IRenderedComponent<SharpMUSH.Client.Pages.WorldMap> cut)
	{
		if (cut.Find(".map-builder input").GetAttribute("value") != "") throw new InvalidOperationException("the place fields still hold level 0's place");
	}

	[Test]
	public async Task APlacesSouthSideReadsTheWallItSharesWithThePlaceBelow()
	{
		Arrange();
		_api.WildsEditable = true;
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 59));
		cut.WaitForAssertion(() => cut.Find(".map-room--place"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.Find(".map-room--place").ClickAsync();

		var selects = cut.FindComponents<MudBlazor.MudSelect<string>>();
		await cut.InvokeAsync(() => selects[0].Instance.ValueChanged.InvokeAsync("s"));

		await Assert.That(cut.FindComponents<MudBlazor.MudSelect<string>>()[1].Instance.Value).IsEqualTo(MapWallKinds.Wall)
			.Because("the wall between 0,1 and 0,0 is listed once, as the north side of 0,0");
	}

	[Test]
	public async Task AHexMapDrawsHexagonsWithWallsOnTheirSixSides()
	{
		Arrange();
		_api.WildsEditable = true;
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 62));
		cut.WaitForAssertion(() => cut.Find("polygon.map-place"));

		await Assert.That(cut.FindAll("polygon.map-place").Count).IsEqualTo(3);
		await Assert.That(cut.FindAll("rect.map-place")).IsEmpty();
		await Assert.That(cut.FindAll("polygon.map-box").Count).IsEqualTo(1).Because("the camp is drawn as a hexagon inside its place");
		await Assert.That(cut.FindAll("rect.map-box")).IsEmpty();
		await Assert.That(cut.FindAll("line.map-grid")).IsEmpty().Because("the hexagons are the grid");
		await Assert.That(cut.FindAll("line.map-wall").Count).IsEqualTo(3);
		await Assert.That(cut.FindAll("line.map-wall--lock").Count).IsEqualTo(1);
		var oneWay = cut.Find("line.map-oneway");
		await Assert.That(double.Parse(oneWay.GetAttribute("y2")!, System.Globalization.CultureInfo.InvariantCulture))
			.IsLessThan(double.Parse(oneWay.GetAttribute("y1")!, System.Globalization.CultureInfo.InvariantCulture))
			.Because("one way out north points up the screen");

		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.Find(".map-room--place").ClickAsync();
		await Assert.That(cut.Find("#map-place-title").TextContent).IsEqualTo("NavMapPlaceAt(1, 0, 0)");
		var selects = cut.FindComponents<MudBlazor.MudSelect<string>>();
		await cut.InvokeAsync(() => selects[0].Instance.ValueChanged.InvokeAsync("sw"));
		await Assert.That(cut.FindComponents<MudBlazor.MudSelect<string>>()[1].Instance.Value).IsEqualTo(MapWallKinds.Wall)
			.Because("the ford's south-west side is the camp's north-east side");
		await Assert.That(cut.FindComponents<MudBlazor.MudTextField<string>>().Select(field => field.Instance.Value)).Contains("Reeds.");
	}

	[Test]
	public async Task APointyHexPlaceStartsOnASideItHas()
	{
		Arrange();
		_api.WildsEditable = true;
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 63));
		cut.WaitForAssertion(() => cut.Find("polygon.map-place"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.Find(".map-room--place").ClickAsync();

		await Assert.That(cut.FindComponents<MudBlazor.MudSelect<string>>()[0].Instance.Value).IsEqualTo("ne")
			.Because("north is a corner on a pointy hex map, not a side");
		await cut.InvokeAsync(() => cut.FindComponents<MudBlazor.MudSelect<string>>()[1].Instance.ValueChanged.InvokeAsync(MapWallKinds.Wall));
		await Click(cut, "NavMapSetSide");
		await Click(cut, "NavMapReview");
		await Assert.That(cut.FindAll(".map-review-commands").Select(commands => commands.TextContent))
			.IsEquivalentTo(["@world/wall #63/1,0,0/ne=wall"]);
	}

	[Test]
	public async Task APlacesDownSideReadsTheLockOfTheLevelBelow()
	{
		Arrange();
		_api.WildsEditable = true;
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 59));
		cut.WaitForAssertion(() => cut.Find(".map-room--place"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.Find(".map-room--place").ClickAsync();

		var selects = cut.FindComponents<MudBlazor.MudSelect<string>>();
		await cut.InvokeAsync(() => selects[0].Instance.ValueChanged.InvokeAsync("d"));

		await Assert.That(cut.FindComponents<MudBlazor.MudSelect<string>>()[1].Instance.Value).IsEqualTo(MapWallKinds.Lock)
			.Because("the map source lists the up side of the place below as this place's down side");
		await Assert.That(cut.Find(".map-builder").TextContent).Contains("NavMapSideLockNote");
		await Click(cut, "NavMapSetSide");
		await Click(cut, "NavMapReview");
		await Assert.That(cut.FindAll(".map-review-commands").Select(commands => commands.TextContent))
			.IsEquivalentTo(["@world/wall #59/0,1,0/d=lock,A trapdoor."]).Because("setting it again keeps the side locked, and its key");
	}

	private async Task<IRenderedComponent<SharpMUSH.Client.Pages.WorldMap>> EditWorksAsync(bool authorized = false)
	{
		Arrange();
		if (authorized) AddAuthorization().SetAuthorized("Sailor");
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 55));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		return cut;
	}

	private static Task Click(IRenderedComponent<SharpMUSH.Client.Pages.WorldMap> cut, string label) =>
		cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == label).ClickAsync();

	[Test]
	public async Task ABuilderRenamesAndDescribesARoomFromItsOwnDescription()
	{
		var cut = await EditWorksAsync();
		await cut.FindAll("button.map-room").First(room => room.TextContent.Trim() == "Lighthouse").ClickAsync();

		cut.WaitForState(() => cut.Find(".map-builder textarea").GetAttribute("value") == "Whitewashed.\nA lamp above.");
		await cut.Find(".map-builder input").InputAsync("Old light");
		await Click(cut, "NavMapSetName");
		await cut.Find(".map-builder textarea").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Whitewashed.\nThe lamp is out." });
		await Click(cut, "NavMapSetDescription");
		await Click(cut, "NavMapReview");

		await Assert.That(cut.FindAll(".map-review-commands").Select(commands => commands.TextContent)).IsEquivalentTo(
			["@name #11=Old light", "&DESCRIBE #11=Whitewashed.%rThe lamp is out."]);
		await Assert.That(cut.Find(".map-review").TextContent).Contains("NavMapPlanRename(Lighthouse, Old light)");
		await Assert.That(_api.Commands).IsEmpty();
	}

	[Test]
	public async Task ABuilderRenamesAnExitDestroysAnotherAndOpensAWayToARoomOnTheMap()
	{
		var cut = await EditWorksAsync();
		await cut.FindAll("button.map-room").First(room => room.TextContent.Trim() == "Lighthouse").ClickAsync();

		await Assert.That(cut.FindAll(".map-builder-list li").Count(item => item.TextContent.Contains("NavMapRename"))).IsEqualTo(1)
			.Because("the builder does not control the way up");
		await Click(cut, "NavMapRename");
		await cut.FindAll(".map-builder-list input").Single().InputAsync("Seaward;sea");
		await cut.FindAll(".map-builder-list button").First(button => button.TextContent.Trim() == "NavMapSetName").ClickAsync();
		await Assert.That(cut.Find(".map-builder").TextContent).Contains("Seaward");

		// No select is open in a test, so the room goes in through the select's own field.
		await cut.InvokeAsync(() => cut.FindComponents<MudBlazor.MudSelect<string>>()
			.Single(select => select.Instance.Class?.Contains("map-builder-join") == true)
			.Instance.ValueChanged.InvokeAsync("#10"));
		await Assert.That(cut.FindAll(".map-builder input").Select(input => input.GetAttribute("value"))).Contains("South;s")
			.Because("the quay is south of the lighthouse, and the old South is Seaward now");
		await Assert.That(cut.Find(".map-builder").TextContent).Contains("NavMapExitTaken").Because("the quay has a North already");
		await cut.FindAll(".map-builder input").Single(input => input.GetAttribute("value") == "North;n").InputAsync("Lighthouse steps;steps");
		await Click(cut, "NavMapAddToPlan");
		await Click(cut, "NavMapReview");

		await Assert.That(cut.FindAll(".map-review-commands").Select(commands => commands.TextContent)).IsEquivalentTo(
			["@name #31=Seaward;sea", "@open South;s=#10,Lighthouse steps;steps,#11"]);
	}

	[Test]
	public async Task AnEmptyCellIsJoinedToTheRoomStraightBesideItNotTheOneChosen()
	{
		var cut = await EditWorksAsync();
		await cut.FindAll("button.map-room").First(room => room.TextContent.Trim() == "Quay").ClickAsync();

		await cut.Find("button.map-cell[aria-label='NavMapCell(1, 1)']").ClickAsync();
		await cut.Find(".map-builder input").InputAsync("Boathouse");
		await Click(cut, "NavMapAddToPlan");
		await Click(cut, "NavMapReview");

		await Assert.That(cut.Find(".map-review-commands").TextContent).Contains("@open East;e=<Boathouse>,West;w,#11")
			.Because("the quay is only at the cell's corner; the lighthouse is beside it");
	}

	[Test]
	public async Task APlanLargerThanTheQuotaLeftCannotBeApplied()
	{
		_api.BuildsLeft = 2;
		var cut = await EditWorksAsync();
		await cut.Find("button.map-cell[aria-label='NavMapCell(1, 0)']").ClickAsync();
		await cut.Find(".map-builder input").InputAsync("Boathouse");
		await Click(cut, "NavMapAddToPlan");
		await Click(cut, "NavMapReview");

		await Assert.That(cut.Find(".map-builder-warning").TextContent).IsEqualTo("NavMapQuotaShort(3, 2)");
		await Assert.That(cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapApply(1)").HasAttribute("disabled"))
			.IsTrue();
	}

	[Test]
	public async Task ApplyingKeepsTheChosenRoomAndItsLevel()
	{
		var cut = await EditWorksAsync();
		await cut.FindAll("button.map-room").First(room => room.TextContent.Trim() == "Lighthouse").ClickAsync();
		await Click(cut, "NavMapSetPlace");
		await Click(cut, "NavMapReview");
		await Click(cut, "NavMapApply(1)");

		cut.WaitForAssertion(() => cut.Find(".map-applied"));
		await Assert.That(cut.Find(".map-builder-title").TextContent).IsEqualTo("Lighthouse");
	}

	[Test]
	public async Task NewMapCreatesTheObjectGivesItAKindAndOpensIt()
	{
		Arrange();
		_api.Here = new("#0", "Room Zero", null) { CanBuild = true };
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>();
		cut.WaitForAssertion(() => cut.FindAll(".map-list button").First(button => button.TextContent.Trim() == "NavMapNewMap"));

		await cut.FindAll(".map-list button").First(button => button.TextContent.Trim() == "NavMapNewMap").ClickAsync();
		await cut.Find(".map-new-form input").InputAsync("Docks");
		await cut.FindAll(".map-new-form button").First(button => button.TextContent.Trim() == "NavMapCreate").ClickAsync();

		cut.WaitForState(() => _api.Commands.Count == 2);
		await Assert.That(_api.Commands).IsEquivalentTo(["@create Docks", "&MAP`KIND #91=compass"]);
		var navigation = Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
		cut.WaitForState(() => navigation.Uri.EndsWith("/map/91", StringComparison.Ordinal));
	}

	[Test]
	public async Task NewMapIsOfferedOnlyToACharacterWhoMayBuild()
	{
		Arrange();
		_api.Here = new("#10", "Quay", WorldMapApiHandler.Harbour);
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 50));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));

		await Assert.That(cut.FindAll(".map-list button").Select(button => button.TextContent.Trim())).DoesNotContain("NavMapNewMap");
	}

	[Test]
	public async Task EditingASparseMapOffersOnlyTheCellsBesideItsRooms()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 56));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));

		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();

		await Assert.That(cut.FindAll("button.map-cell")).Count().IsEqualTo(16)
			.Because("the eight cells around each room, not the hundred million in the square between them");
	}

	[Test]
	public async Task MovingARoomShowsItWhereItWillBe()
	{
		Arrange();
		var cut = Render<SharpMUSH.Client.Pages.WorldMap>(parameters => parameters.Add(p => p.Id, 55));
		cut.WaitForAssertion(() => cut.Find(".map-room--here"));
		await cut.FindAll("button").First(button => button.TextContent.Trim() == "NavMapEdit").ClickAsync();
		await cut.FindAll("button.map-room").First(room => room.TextContent.Trim() == "Lighthouse").ClickAsync();
		await Assert.That(cut.Find(".map-builder-x input").GetAttribute("value")).IsEqualTo("0");

		await cut.Find("button.map-cell[aria-label='NavMapCell(1, 1)']").ClickAsync();
		await cut.FindAll(".map-builder button").First(button => button.TextContent.Trim() == "NavMapMoveHere(Lighthouse)").ClickAsync();

		await Assert.That(cut.Find(".map-builder-x input").GetAttribute("value")).IsEqualTo("1").Because("the fields follow the room");
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
	public async Task ACompassHexMapStepsToTheHexagonsAnExitFaces()
	{
		var layout = new MapLayout(Map(MapKinds.Compass,
			[new("#1", "Ford", [2, 2, 0], null), new("#2", "Bank", null, null), new("#3", "Reeds", null, null)],
			[Exit(10, "ne", "#1", "#2"), Exit(11, "Southwest", "#1", "#3")]) with
		{ Space = new(MapSpaces.Hex, -5, -5, 5, 5, [0]) });

		await Assert.That(layout.Spot(layout.Rooms["#2"])).IsEqualTo((3.0, 2.0, 0)).Because("a hex's northeast side faces (x+1, y)");
		await Assert.That(layout.Spot(layout.Rooms["#3"])).IsEqualTo((1.0, 2.0, 0)).Because("a hex's southwest side faces (x-1, y)");
	}

	[Test]
	public async Task ACompassPointyHexMapStepsToTheHexagonsAnExitFaces()
	{
		var layout = new MapLayout(Map(MapKinds.Compass,
			[new("#1", "Ford", [2, 2, 0], null), new("#2", "Bank", null, null), new("#3", "Reeds", null, null)],
			[Exit(10, "ne", "#1", "#2"), Exit(11, "North", "#2", "#3")]) with
		{ Space = new(MapSpaces.PointyHex, -5, -5, 5, 5, [0]) });

		await Assert.That(layout.Spot(layout.Rooms["#2"])).IsEqualTo((2.0, 3.0, 0)).Because("a pointy hex's northeast side faces (x, y+1)");
		await Assert.That(layout.Spot(layout.Rooms["#3"])).IsEqualTo((1.0, 5.0, 0)).Because("north goes round a corner to (x-1, y+2)");
	}

	[Test]
	public async Task ExitNamesGiveAHexMapsAxialSteps()
	{
		await Assert.That(MapLayout.Direction(new("#1", "Northeast", [], "#1", "#2", null), MapSpaces.Hex)).IsEqualTo((1, 0, 0));
		await Assert.That(MapLayout.Direction(new("#1", "Out", ["sw"], "#1", "#2", null), MapSpaces.Hex)).IsEqualTo((-1, 0, 0));
		await Assert.That(MapLayout.Direction(new("#1", "East", [], "#1", "#2", null), MapSpaces.Hex)).IsEqualTo((2, -1, 0))
			.Because("east goes round a flat hex's corner");
		await Assert.That(MapLayout.Direction(new("#1", "Northeast", [], "#1", "#2", null), MapSpaces.PointyHex)).IsEqualTo((0, 1, 0));
		await Assert.That(MapLayout.Direction(new("#1", "East", [], "#1", "#2", null), MapSpaces.PointyHex)).IsEqualTo((1, 0, 0));
		await Assert.That(MapLayout.Direction(new("#1", "n", [], "#1", "#2", null), MapSpaces.PointyHex)).IsEqualTo((-1, 2, 0))
			.Because("north goes round a pointy hex's corner");
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
	public async Task ASpotIsWhereTheMapDrewTheRoom()
	{
		var compass = new MapLayout(Map(MapKinds.Compass,
			[new("#1", "Gate", [5, 5, 0], null), new("#2", "Road", null, null), new("#3", "Loft", null, null)],
			[Exit(10, "n", "#1", "#2"), Exit(11, "up", "#2", "#3")]));
		var hall = new MapLayout(Map(MapKinds.Coordinates, [new("#1", "Hall", [2, 3, 0], [2, 3])], []));
		var square = new MapLayout(Map(MapKinds.Graph, [new("#1", "Square", [200, 100], null)], []));

		await Assert.That(compass.Spot(compass.Rooms["#2"])).IsEqualTo((5.0, 6.0, 0)).Because("Road has no cell stored; its exit puts it there");
		await Assert.That(compass.Spot(compass.Rooms["#3"])).IsEqualTo((5.0, 6.0, 1));
		await Assert.That(hall.Spot(hall.Rooms["#1"])).IsEqualTo((2.0, 3.0, 0)).Because("a room's spot is its south-west cell");
		await Assert.That(square.Spot(square.Rooms["#1"])).IsEqualTo((200.0, 100.0, 0));
	}

	[Test]
	public async Task ExitNamesAndAliasesGiveTheirDirection()
	{
		await Assert.That(MapLayout.Direction(new("#1", "Out", ["o"], "#1", "#2", null))).IsNull();
		await Assert.That(MapLayout.Direction(new("#1", "Ladder", ["U"], "#1", "#2", null))).IsEqualTo((0, 0, 1));
		await Assert.That(MapLayout.Direction(new("#1", "Southwest", [], "#1", "#2", null))).IsEqualTo((-1, -1, 0));
	}

	[Test]
	public async Task StoredPlacesWidenTheDrawingAndAddTheirLevels()
	{
		var layout = new MapLayout(Map(MapKinds.Coordinates, [new("#1", "Camp", [0, 0, 0], null)], []) with
		{
			Space = new(MapSpaces.Square, -3, -2, 4, 5, [-1, 0]),
		});

		await Assert.That(layout.Levels).IsEquivalentTo([-1, 0]);
		await Assert.That(layout.Bounds(0)).IsEqualTo((-3 * MapLayout.Cell, -6 * MapLayout.Cell, 5 * MapLayout.Cell, 2 * MapLayout.Cell));
		await Assert.That(layout.Bounds(0, includePlaces: false)).IsEqualTo((0d, -MapLayout.Cell, MapLayout.Cell, 0d));
		await Assert.That(new MapLayout(Map(MapKinds.Graph, [], []) with { Space = new(MapSpaces.Square, 0, 0, 1, 1, [0]) }).Space).IsNull()
			.Because("a picture places rooms by its pixels, not by cells");
		await Assert.That(new MapLayout(Map(MapKinds.Coordinates, [], []) with { Space = new(MapSpaces.Graph, 0, 0, 1, 1, [0]) }).Space).IsNull()
			.Because("this portal draws square and hex places only");
	}

	[Test]
	public async Task AHexMapsPlacesAreFlatToppedHexagonsAtTheirAxialCoordinates()
	{
		var layout = new MapLayout(Map(MapKinds.Coordinates, [new("#1", "Camp", [1, 0, 0], null)], []) with
		{
			Space = new(MapSpaces.Hex, -2, -2, 2, 2, [0]),
		});
		var (s, h) = (MapLayout.HexSide, MapLayout.HexHeight);

		await Assert.That(layout.IsHex).IsTrue();
		await Assert.That(layout.PlaceCentre(0, 1)).IsEqualTo((0d, -h)).Because("north is a row up");
		await Assert.That(layout.PlaceCentre(1, 0)).IsEqualTo((1.5 * s, -h / 2)).Because("northeast is a column east and half a row up");
		foreach (var (x, y) in new[] { (0, 0), (1, 0), (1, -1), (-1, 1), (-2, 2), (2, -2), (0, -2) })
		{
			var (cx, cy) = layout.PlaceCentre(x, y);
			await Assert.That(layout.PlaceAt(cx, cy)).IsEqualTo((x, y));
			await Assert.That(layout.PlaceAt(cx + 0.9 * h / 2, cy)).IsEqualTo((x, y)).Because("a point inside the hexagon is on it");
		}
		await Assert.That(layout.PlaceAt(0.95 * s, 0)).IsEqualTo((0, 0));
		await Assert.That(layout.PlaceAt(1.05 * s, -h / 4)).IsEqualTo((1, 0)).Because("past the east corner is the northeast place");

		var camp = layout.Rooms["#1"];
		await Assert.That((camp.X + camp.Width / 2, camp.Y + camp.Height / 2)).IsEqualTo(layout.PlaceCentre(1, 0));
		await Assert.That(layout.Spot(camp)).IsEqualTo((1d, 0d, 0));

		await Assert.That(layout.HexEdge(0, 0, "e")).IsNull();
		var north = layout.HexEdge(0, 0, "n")!.Value;
		await Assert.That(new[] { north.Y1, north.Y2, north.Dx, north.Dy }.Select(value => Math.Round(value, 9)))
			.IsEquivalentTo([Math.Round(-h / 2, 9), Math.Round(-h / 2, 9), 0d, -1d]);
		var southwest = layout.HexEdge(1, 0, "sw")!.Value;
		var northeast = layout.HexEdge(0, 0, "ne")!.Value;
		await Assert.That(Math.Abs(southwest.X1 - northeast.X2) + Math.Abs(southwest.Y1 - northeast.Y2)
				+ Math.Abs(southwest.X2 - northeast.X1) + Math.Abs(southwest.Y2 - northeast.Y1)).IsLessThan(1e-9)
			.Because("a place's southwest edge is its neighbour's northeast edge");

		// The places' box runs from the west corner of the west column to the east corner of the east one, and from the
		// north side of the north-east corner's place to the south side of the south-west one's.
		var bounds = layout.Bounds(0);
		await Assert.That(bounds.Left).IsEqualTo(-3 * s - s);
		await Assert.That(bounds.Right).IsEqualTo(3 * s + s);
		await Assert.That(bounds.Top).IsEqualTo(-(2 + 1) * h - h / 2);
		await Assert.That(bounds.Bottom).IsEqualTo((2 + 1) * h + h / 2);

		var view = layout.PlacesIn(-s, -h, s, h);
		await Assert.That(view.Left <= -1 && view.Right >= 1 && view.Bottom <= -1 && view.Top >= 1).IsTrue()
			.Because($"a view around the middle holds the places around it, not {view}");
	}

	[Test]
	public async Task APointyHexMapsPlacesArePointyToppedHexagonsAtTheirAxialCoordinates()
	{
		var layout = new MapLayout(Map(MapKinds.Coordinates, [new("#1", "Camp", [0, 1, 0], null)], []) with
		{
			Space = new(MapSpaces.PointyHex, -2, -2, 2, 2, [0]),
		});
		var (s, h) = (MapLayout.HexSide, MapLayout.HexHeight);

		await Assert.That((layout.IsHex, layout.IsPointy)).IsEqualTo((true, true));
		await Assert.That(layout.PlaceCentre(1, 0)).IsEqualTo((h, -0d)).Because("east is a place's width east");
		await Assert.That(layout.PlaceCentre(0, 1)).IsEqualTo((h / 2, -1.5 * s)).Because("northeast is a row up and half a place east");
		foreach (var (x, y) in new[] { (0, 0), (1, 0), (1, -1), (-1, 1), (-2, 2), (2, -2), (0, -2) })
		{
			var (cx, cy) = layout.PlaceCentre(x, y);
			await Assert.That(layout.PlaceAt(cx, cy)).IsEqualTo((x, y));
			await Assert.That(layout.PlaceAt(cx, cy - 0.9 * s)).IsEqualTo((x, y)).Because("a point inside the hexagon is on it");
		}
		await Assert.That(layout.PlaceAt(0.1 * s, -1.05 * s)).IsEqualTo((0, 1)).Because("past the north corner, to its east, is the northeast place");

		var camp = layout.Rooms["#1"];
		await Assert.That((camp.X + camp.Width / 2, camp.Y + camp.Height / 2)).IsEqualTo(layout.PlaceCentre(0, 1));
		await Assert.That(layout.Spot(camp)).IsEqualTo((0d, 1d, 0));

		await Assert.That(layout.HexEdge(0, 0, "n")).IsNull();
		var east = layout.HexEdge(0, 0, "e")!.Value;
		await Assert.That(new[] { east.X1, east.X2, east.Dx, east.Dy }.Select(value => Math.Round(value, 9)))
			.IsEquivalentTo([Math.Round(h / 2, 9), Math.Round(h / 2, 9), 1d, 0d]);
		var southwest = layout.HexEdge(0, 1, "sw")!.Value;
		var northeast = layout.HexEdge(0, 0, "ne")!.Value;
		await Assert.That(Math.Abs(southwest.X1 - northeast.X2) + Math.Abs(southwest.Y1 - northeast.Y2)
				+ Math.Abs(southwest.X2 - northeast.X1) + Math.Abs(southwest.Y2 - northeast.Y1)).IsLessThan(1e-9)
			.Because("a place's southwest edge is its neighbour's northeast edge");

		var bounds = layout.Bounds(0);
		await Assert.That((bounds.Left, bounds.Right)).IsEqualTo(((-2 - 1) * h - h / 2, (2 + 1) * h + h / 2));
		await Assert.That((bounds.Top, bounds.Bottom)).IsEqualTo((-3 * s - s, 3 * s + s));
		var view = layout.PlacesIn(-h, -s, h, s);
		await Assert.That(view.Left <= -1 && view.Right >= 1 && view.Bottom <= -1 && view.Top >= 1).IsTrue()
			.Because($"a view around the middle holds the places around it, not {view}");
	}

	[Test]
	public async Task AWindowOfPlacesIsTheViewAndHalfAgainWithinTheStoredPlacesAndAtMost128ASide()
	{
		var layout = new MapLayout(Map(MapKinds.Coordinates, [], []) with
		{
			Space = new(MapSpaces.Square, -1000, -1000, 1000, 1000, [0]),
		});

		await Assert.That(layout.Window((0, 0, 9, 9))).IsEqualTo((-5, -5, 14, 14));
		await Assert.That(layout.Window((-1000, -1000, -991, -991))).IsEqualTo((-1000, -1000, -986, -986));
		await Assert.That(layout.Window((-200, 0, 199, 9))).IsEqualTo((-64, -5, 63, 14))
			.Because("a view wider than one read holds is read around its middle");
		await Assert.That(layout.Window((2000, 0, 2010, 10))).IsNull();
		await Assert.That(MapLayout.CellsIn(-1, -2 * MapLayout.Cell + 1, MapLayout.Cell, 0)).IsEqualTo((-1, 0, 1, 1));
	}

	[Test]
	public async Task PlacesReadAlreadyHoldAViewInsideThemOrTheMostOneReadMayHold()
	{
		var layout = new MapLayout(Map(MapKinds.Coordinates, [], []) with
		{
			Space = new(MapSpaces.Square, -1000, -1000, 1000, 1000, [0, 1]),
		});
		var read = new MapCellsDto(0, -5, -5, 14, 14, [], [], false);

		await Assert.That(layout.Holds(read, 0, (0, 0, 9, 9))).IsTrue();
		await Assert.That(layout.Holds(read, 1, (0, 0, 9, 9))).IsFalse().Because("it is another level's");
		await Assert.That(layout.Holds(read, 0, (10, 0, 19, 9))).IsFalse();
		await Assert.That(layout.Holds(null, 0, (5000, 5000, 5001, 5001))).IsTrue().Because("there is nothing there to read");
		var widest = new MapCellsDto(0, -64, -5, 63, 14, [], [], false);
		await Assert.That(layout.Holds(widest, 0, (-200, 0, 199, 9))).IsTrue();
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
