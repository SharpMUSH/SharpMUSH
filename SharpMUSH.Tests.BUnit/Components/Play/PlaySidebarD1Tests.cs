using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components.Characters;

namespace SharpMUSH.Tests.BUnit.Components.Play;

/// <summary>
/// README §5.1 Play page sidebar (boards 01 and 07): "Play · ● Connected as {name}", In scene (the
/// room's picture, the scene title and its cast), Channels and Pages from the comm feed, and the
/// collapsed strip.
/// </summary>
public class PlaySidebarD1Tests : TrackingBunitContext
{
	private readonly TestCommFeed _feed = new();
	private readonly CharactersApiFake _api;

	public PlaySidebarD1Tests()
	{
		(_api, var factory, _) = CharactersApiFake.Install(this);
		Services.AddSingleton<ICommFeed>(_feed);
		Services.AddSingleton(new ApplicationRegistryClient(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplicationRegistryClient>.Instance));
		_api.Extra["/api/applications"] = "[]";
	}

	private static readonly RoomInfo Docks = new("#1201", "Lower Docks", "#1201:1", "Harbour Ward",
		new ImageRef("/api/wiki-assets/r/docks.jpg", "The quay", null, null, null), null,
		new RoomScene("42", "Salt Market at Dusk", 5));

	private IRenderedComponent<PlaySidebar> RenderSidebar(RoomInfo? room = null, bool connected = true, bool collapsed = false,
		string? current = null, Action? onScene = null, Action<string>? onOpen = null, string? characterName = "Ilsa Varn") =>
		Render<PlaySidebar>(p => p
			.Add(x => x.CharacterName, characterName)
			.Add(x => x.Connected, connected)
			.Add(x => x.Room, room)
			.Add(x => x.Collapsed, collapsed)
			.Add(x => x.Current, current)
			.Add(x => x.OnOpenScene, () => onScene?.Invoke())
			.Add(x => x.OnOpen, key => onOpen?.Invoke(key)));

	[Test]
	public async Task Head_SaysWhoIsConnected()
	{
		var cut = RenderSidebar();
		await Assert.That(cut.Find(".kit-side-title").TextContent).IsEqualTo("Play");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).Contains("Connected as Ilsa Varn");
		await Assert.That(cut.FindAll(".kit-side-sub .kit-dot:not(.kit-dot--off)").Count).IsEqualTo(1);

		var offline = RenderSidebar(connected: false);
		await Assert.That(offline.Find(".kit-side-sub").TextContent).Contains("Disconnected");
		await Assert.That(offline.FindAll(".kit-side-sub .kit-dot:not(.kit-dot--off)").Count).IsEqualTo(0);
	}

	/// <summary>
	/// A visitor's socket is open before anyone is logged in on it (the guest login screen): the dot is
	/// lit, so the line beside it must not say "Disconnected".
	/// </summary>
	[Test]
	public async Task Head_ConnectedWithNoCharacter_SaysConnected()
	{
		var cut = RenderSidebar(characterName: null);
		await Assert.That(cut.Find(".kit-side-sub").TextContent).Contains("Connected");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).DoesNotContain("Disconnected");
		await Assert.That(cut.FindAll(".kit-side-sub .kit-dot:not(.kit-dot--off)").Count).IsEqualTo(1);
	}

	[Test]
	public async Task InScene_IsTheRoomPicture_TheSceneTitle_AndItsCast_AndOpensTheScene()
	{
		var opened = false;
		var cut = RenderSidebar(Docks, onScene: () => opened = true);
		var row = cut.Find(".play-side-scene .kit-row");
		await Assert.That(row.QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/r/docks.jpg");
		await Assert.That(row.QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(row.QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("5");
		await Assert.That(row.GetAttribute("aria-current")).IsEqualTo("page").Because("the scene is what main shows");
		await row.ClickAsync();
		await Assert.That(opened).IsTrue();
	}

	[Test]
	public async Task OutsideAScene_ThereIsNoInSceneGroup()
	{
		var cut = RenderSidebar(Docks with { Scene = null });
		await Assert.That(cut.FindAll(".play-side-scene").Count).IsEqualTo(0);
		await Assert.That(cut.Markup).DoesNotContain("In scene");
	}

	[Test]
	public async Task AnUnsafeRoomPicture_IsNotRendered()
	{
		var cut = RenderSidebar(Docks with { Image = new ImageRef("javascript:alert(1)", null, null, null, null) });
		await Assert.That(cut.Find(".play-side-scene .kit-row").QuerySelector("img")).IsNull();
	}

	[Test]
	public async Task Channels_AreHashRows_WithUnreadPills_AndOpenInMain()
	{
		_feed.ChannelList = [new CommChannel("Public", 3), new CommChannel("Newcomers", 0), new CommChannel("Staff", 4, Joined: false)];
		string? opened = null;
		var cut = RenderSidebar(onOpen: k => opened = k, current: "Newcomers");
		var rows = cut.FindAll(".play-side-channels .kit-row");
		await Assert.That(rows.Count).IsEqualTo(2).Because("a channel the viewer has not joined is not listed");
		await Assert.That(rows[0].ClassList).Contains("kit-row--channel");
		await Assert.That(rows[0].ClassList).Contains("kit-row--unread");
		await Assert.That(rows[0].QuerySelector(".kit-row-unread")!.TextContent).IsEqualTo("3");
		await Assert.That(rows[1].ClassList).DoesNotContain("kit-row--unread");
		await Assert.That(rows[1].GetAttribute("aria-current")).IsEqualTo("page");
		await rows[0].ClickAsync();
		await Assert.That(opened).IsEqualTo("Public");
	}

	[Test]
	public async Task Pages_OnePerson_HasTheirPortraitAndFullName_AGroupStacksTwo()
	{
		var at = DateTimeOffset.UtcNow;
		_feed.ConversationList =
		[
			new CommConversation("#314:1", ["Wren Halloway"], ["#314:1"], 2, at),
			new CommConversation("#312:1|#315:1", ["Tomas Reyes", "Dace Kellan"], ["#312:1", "#315:1"], 1, at),
		];
		string? opened = null;
		var cut = RenderSidebar(onOpen: k => opened = k);
		cut.WaitForAssertion(() => cut.Find(".play-side-pages img.kit-row-avatar"), TimeSpan.FromSeconds(5));
		var rows = cut.FindAll(".play-side-pages .kit-row");
		await Assert.That(rows[0].QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Wren Halloway");
		await Assert.That(rows[0].QuerySelector(".kit-row-unread")!.TextContent).IsEqualTo("2");
		await Assert.That(rows[1].QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Tomas, Dace");
		await Assert.That(rows[1].QuerySelector("img.kit-row-avatar")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/t/tomas.jpg")
			.Because("the picture comes from the directory row with that objid");
		await Assert.That(rows[1].QuerySelector(".kit-row-avatar--second")!.TextContent).IsEqualTo("DK");
		await rows[1].ClickAsync();
		await Assert.That(opened).IsEqualTo("#312:1|#315:1");
	}

	/// <summary>
	/// A new player with no scene, channel or page still sees the three groups, each saying how to fill
	/// it, so the drawer is not just a Menu button.
	/// </summary>
	[Test]
	public async Task NothingYet_ListsEachGroup_WithHowToFillIt()
	{
		var cut = RenderSidebar();
		cut.WaitForAssertion(() => cut.Find(".play-side-scene-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-side-channels, .play-side-pages, .play-side-scene").Count).IsEqualTo(0)
			.Because("there are no rows to list");
		var labels = cut.FindAll(".kit-section-label").Select(l => l.TextContent.Trim()).ToList();
		await Assert.That(labels).Contains("Scenes");
		await Assert.That(labels).Contains("Channels");
		await Assert.That(labels).Contains("Pages");
		await Assert.That(cut.Find(".play-side-scene-empty a").GetAttribute("href")).IsEqualTo("/scenes");
		await Assert.That(cut.FindAll(".play-side-channels-empty code").Select(c => c.TextContent).ToList())
			.IsEquivalentTo(["@channel/list", "@channel/on <channel>"]);
		await Assert.That(cut.Find(".play-side-pages-empty code").TextContent).IsEqualTo("page <name>=<message>");
		await Assert.That(cut.Find(".play-side-pages-empty").TextContent).DoesNotContain("`");
	}

	[Test]
	public async Task NothingYet_WithoutTheSceneSystem_HasNoScenesGroup()
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true, features: []));
		var cut = RenderSidebar();
		cut.WaitForAssertion(() => cut.Find(".play-side-channels-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-side-scene-empty").Count).IsEqualTo(0);
	}

	[Test]
	public async Task InAScene_ShowsNoScenesHint()
		=> await Assert.That(RenderSidebar(Docks).FindAll(".play-side-scene-empty").Count).IsEqualTo(0);

	[Test]
	public async Task Collapsed_NothingYet_ShowsNoEmptyGroups()
	{
		var cut = RenderSidebar(collapsed: true);
		await Assert.That(cut.FindAll(".play-side-empty, .kit-section-label").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheFeedChanging_Rerenders()
	{
		var cut = RenderSidebar();
		_feed.ChannelList = [new CommChannel("Public", 1)];
		_feed.Raise();
		cut.WaitForAssertion(() => cut.Find(".play-side-channels .kit-row"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-side-channels .kit-row-label").TextContent).IsEqualTo("Public");
	}

	[Test]
	public async Task Collapsed_KeepsOnlyTheLeads()
	{
		_feed.ChannelList = [new CommChannel("Public", 3)];
		var cut = RenderSidebar(Docks, collapsed: true);
		await Assert.That(cut.FindAll(".kit-side-title").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-row-label").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".play-side-channels .kit-row-hash").TextContent).IsEqualTo("#P");
		await Assert.That(cut.Find(".play-side-channels .kit-row-unread--badge").TextContent).IsEqualTo("3");
	}

	[Test]
	public async Task PageApps_PlacedInPlay_AreListedUnderApps()
	{
		// README §7.4: page apps with NavPlacement "Play" are listed at the foot of the Play sidebar.
		_api.Extra["/api/applications"] = """
			[{"slug":"weather-map","displayName":"Weather map","icon":null,"kind":"Page","schemaUrl":"http/weather/schema","dataUrl":null,
			  "submitRoute":null,"minimumRole":"Guest","navPlacement":"Play","zones":[],"order":1},
			 {"slug":"builder","displayName":"Builder","icon":null,"kind":"Page","schemaUrl":"http/b/schema","dataUrl":null,
			  "submitRoute":null,"minimumRole":"Guest","navPlacement":"Build","zones":[],"order":1}]
			""";
		var cut = RenderSidebar();
		cut.WaitForAssertion(() => cut.Find(".play-side-apps .kit-row"), TimeSpan.FromSeconds(5));
		var rows = cut.FindAll(".play-side-apps .kit-row");
		await Assert.That(rows.Count).IsEqualTo(1);
		await Assert.That(rows[0].GetAttribute("href")).IsEqualTo("/apps/weather-map");
		await Assert.That(rows[0].TextContent).Contains("Weather map");
	}

	[Test]
	public async Task NoPlayApps_NoAppsGroup()
		=> await Assert.That(RenderSidebar().FindAll(".play-side-apps").Count).IsEqualTo(0);

	[Test]
	public async Task Disposing_StopsListeningToTheFeed()
	{
		var cut = RenderSidebar();
		await Assert.That(_feed.Listeners).IsEqualTo(1);
		cut.Instance.Dispose();
		await Assert.That(_feed.Listeners).IsEqualTo(0);
	}
}
