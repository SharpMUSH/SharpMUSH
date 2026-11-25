using Microsoft.AspNetCore.Components;
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
	private readonly TestCommGroupStore _groupStore = new();
	private readonly CharactersApiFake _api;

	public PlaySidebarD1Tests()
	{
		(_api, var factory, _) = CharactersApiFake.Install(this);
		Services.AddSingleton<ICommFeed>(_feed);
		Services.AddSingleton<ICommGroupStore>(_groupStore);
		Services.AddSingleton<CommGroups>();
		Services.AddSingleton<ChannelBrowserService>();
		Services.AddSingleton(new ApplicationRegistryClient(factory, Microsoft.Extensions.Logging.Abstractions.NullLogger<ApplicationRegistryClient>.Instance));
		_api.Extra["/api/applications"] = "[]";
	}

	private static readonly RoomInfo Docks = new("#1201", "Lower Docks", "#1201:1", "Harbour Ward",
		new ImageRef("/api/wiki-assets/r/docks.jpg", "The quay", null, null, null), null,
		new RoomScene("42", "Salt Market at Dusk", 5));

	private IRenderedComponent<PlaySidebar> RenderSidebar(RoomInfo? room = null, bool connected = true, bool collapsed = false,
		string? current = null, Action<string>? onScene = null, Action<string>? onOpen = null, string? characterName = "Ilsa Varn", Action? onGame = null) =>
		Render<PlaySidebar>(p => p
			.Add(x => x.CharacterName, characterName)
			.Add(x => x.Connected, connected)
			.Add(x => x.Room, room)
			.Add(x => x.Collapsed, collapsed)
			.Add(x => x.Current, current)
			.Add(x => x.OnOpenScene, id => onScene?.Invoke(id))
			.Add(x => x.OnOpen, key => onOpen?.Invoke(key))
			.Add(x => x.OnOpenGame, () => onGame?.Invoke()));

	[Test]
	public async Task WithNoScene_TheRoomRow_LeadsBackToTheGame()
	{
		var back = false;
		var room = new RoomInfo("#0", "Room Zero", null, null, null, null, null);
		var cut = RenderSidebar(room, current: "Public", onGame: () => back = true);
		var row = cut.Find(".play-side-room .kit-row");
		await Assert.That(row.QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Room Zero");
		await Assert.That(row.GetAttribute("aria-current")).IsNull().Because("a channel is what main shows");
		await row.ClickAsync();
		await Assert.That(back).IsTrue();

		var playing = RenderSidebar(room, current: string.Empty);
		await Assert.That(playing.Find(".play-side-room .kit-row").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(RenderSidebar(Docks).FindAll(".play-side-room")).IsEmpty().Because("the scene row is the way back there");
	}

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
		var cut = RenderSidebar(Docks, onScene: _ => opened = true);
		var row = cut.Find(".play-side-scene .kit-row");
		await Assert.That(row.QuerySelector("img")!.GetAttribute("src")).IsEqualTo("/api/wiki-assets/r/docks.jpg");
		await Assert.That(row.QuerySelector(".kit-row-label")!.TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(row.QuerySelector(".kit-row-count")!.TextContent).IsEqualTo("5");
		await Assert.That(row.GetAttribute("aria-current")).IsEqualTo("page").Because("the scene is what main shows");
		await row.ClickAsync();
		await Assert.That(opened).IsTrue();
	}

	/// <summary>A room running several scenes gives each its own row, which opens that scene; the page's scene is current.</summary>
	[Test]
	public async Task SeveralScenes_EachHaveTheirOwnRow_AndOpenThatScene()
	{
		string? opened = null;
		var room = Docks with
		{
			Scene = new RoomScene("43", "Private scene test", 1),
			Scenes = [new RoomScene("42", "Salt Market at Dusk", 5), new RoomScene("43", "Private scene test", 1)],
		};
		var cut = RenderSidebar(room, onScene: id => opened = id);

		var rows = cut.FindAll(".play-side-scene .kit-row");
		await Assert.That(rows.Select(r => r.QuerySelector(".kit-row-label")!.TextContent))
			.IsEquivalentTo(new[] { "Salt Market at Dusk", "Private scene test" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(rows.Select(r => r.QuerySelector(".kit-row-count")!.TextContent))
			.IsEquivalentTo(new[] { "5", "1" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(rows[0].GetAttribute("aria-current")).IsNull();
		await Assert.That(rows[1].GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.FindAll(".play-side-scene").Count).IsEqualTo(1).Because("the rows share one In scene group");

		await rows[0].ClickAsync();
		await Assert.That(opened).IsEqualTo("42");
	}

	/// <summary>Each scene row marks whether the viewer's poses go there: a check in the success colour, or an empty circle.</summary>
	[Test]
	public async Task EachSceneRow_MarksWhetherTheViewerIsActiveInIt()
	{
		var room = Docks with
		{
			Scene = new RoomScene("42", "Salt Market at Dusk", 5, "participant", true),
			Scenes =
			[
				new RoomScene("42", "Salt Market at Dusk", 5, "participant", true),
				new RoomScene("43", "Private scene test", 1, "participant", false, Elsewhere: true),
				new RoomScene("44", "Card game", 2, null, false, Elsewhere: true),
			],
		};
		var cut = RenderSidebar(room);

		var rows = cut.FindAll(".play-side-scene .kit-row");
		await Assert.That(rows[0].ClassList).Contains("play-side-scene-row--active");
		await Assert.That(rows[0].QuerySelector(".play-side-scene-status--active")!.TextContent.Trim()).IsEqualTo("Active");
		await Assert.That(rows[1].ClassList).Contains("play-side-scene-row--inactive");
		await Assert.That(rows[1].QuerySelector(".play-side-scene-status--inactive")!.TextContent.Trim()).IsEqualTo("Inactive");
		await Assert.That(rows[2].ClassList).Contains("play-side-scene-row--out");
		await Assert.That(rows[2].QuerySelector(".play-side-scene-status--out")!.TextContent.Trim()).IsEqualTo("Not joined");
		await Assert.That(RenderSidebar(Docks).FindAll(".play-side-scene-status")).IsEmpty()
			.Because("a handler that does not say where the viewer's focus is gets no mark");
	}

	[Test]
	public async Task ACollapsedSceneRow_SaysWhetherTheViewerIsActiveInIt()
	{
		var room = Docks with
		{
			Scene = new RoomScene("42", "Salt Market at Dusk", 5, "participant", true),
			Scenes =
			[
				new RoomScene("42", "Salt Market at Dusk", 5, "participant", true),
				new RoomScene("43", "Private scene test", 1, "participant", false, Elsewhere: true),
			],
		};
		var rows = RenderSidebar(room, collapsed: true).FindAll(".play-side-scene .kit-row");

		await Assert.That(rows[0].GetAttribute("aria-label")).IsEqualTo("Salt Market at Dusk, Active")
			.Because("a collapsed row draws no second line, so its name carries the status");
		await Assert.That(rows[1].GetAttribute("aria-label")).IsEqualTo("Private scene test, Inactive");
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
	/// Each page row says whether its people are connected, so it is plain before writing whether a page can be
	/// delivered: a ring while on, dimmed while not, and the same in words for a screen reader.
	/// </summary>
	[Test]
	public async Task Pages_RingWhoIsConnected_AndDimWhoIsNot()
	{
		var at = DateTimeOffset.UtcNow;
		_feed.ConversationList =
		[
			new CommConversation("#314:1", ["Wren Halloway"], ["#314:1"], 0, at),
			new CommConversation("#316:1", ["Magister Oake"], ["#316:1"], 0, at),
			new CommConversation("#312:1|#315:1", ["Tomas Reyes", "Dace Kellan"], ["#312:1", "#315:1"], 0, at),
		];
		var cut = RenderSidebar();
		cut.WaitForAssertion(() => cut.Find(".play-side-pages .kit-row-avatar--online"), TimeSpan.FromSeconds(5));
		var rows = cut.FindAll(".play-side-pages .kit-row");

		await Assert.That(rows[0].QuerySelector(".kit-row-avatar")!.ClassList.Contains("kit-row-avatar--online")).IsTrue();
		await Assert.That(rows[0].TextContent).Contains("online");
		await Assert.That(rows[1].QuerySelector(".kit-row-avatar")!.ClassList.Contains("kit-row-avatar--offline")).IsTrue();
		await Assert.That(rows[1].TextContent).Contains("offline");
		await Assert.That(rows[2].QuerySelector(".kit-row-avatar:not(.kit-row-avatar--second)")!.ClassList.Contains("kit-row-avatar--online")).IsTrue();
		await Assert.That(rows[2].QuerySelector(".kit-row-avatar--second")!.ClassList.Contains("kit-row-avatar--offline")).IsTrue();
		await Assert.That(rows[2].TextContent).Contains("1 of 2 online");
	}

	/// <summary>An online list that could not be read marks nobody either way.</summary>
	[Test]
	public async Task Pages_WithNoOnlineList_ShowNoPresence()
	{
		_api.Online = "not json";
		var asked = false;
		_api.OnRequest = request =>
		{
			if (request.RequestUri!.AbsolutePath == "/http/online") asked = true;
			return Task.CompletedTask;
		};
		_feed.ConversationList = [new CommConversation("#314:1", ["Wren Halloway"], ["#314:1"], 0, DateTimeOffset.UtcNow)];
		var cut = RenderSidebar();
		cut.WaitForAssertion(() => cut.Find(".play-side-pages .kit-row"), TimeSpan.FromSeconds(5));
		cut.WaitForState(() => asked, TimeSpan.FromSeconds(5));
		// The unreadable answer is taken in on the renderer's dispatcher after the request returns.
		await cut.InvokeAsync(() => { });
		await cut.InvokeAsync(() => { });
		await Assert.That(cut.FindAll(".kit-row-avatar--online, .kit-row-avatar--offline")).IsEmpty();
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
		await Assert.That(cut.FindAll(".play-side-scene-empty a").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".play-side-channels-empty").TextContent).Contains("haven't joined any channels");
		var browse = cut.Find(".play-side-head .play-side-browse");
		await Assert.That(browse.GetAttribute("aria-label")).IsEqualTo("Browse channels");
		await Assert.That(browse.GetAttribute("aria-expanded")).IsEqualTo("false");
		await Assert.That(browse.TextContent.Trim()).IsEqualTo("Browse");
		await Assert.That(cut.Find(".play-side-pages-empty code").TextContent).IsEqualTo("page <name>=<message>");
		await Assert.That(cut.Find(".play-side-pages-empty").TextContent).DoesNotContain("`");
	}

	/// <summary>With scene-rooms installed, the empty Scenes group links to the start form set to run the scene in a copy of this room.</summary>
	[Test]
	public async Task NothingYet_WithRoomCopies_LinksToStartingASceneInACopy()
	{
		var cut = Render<PlaySidebar>(p => p.Add(x => x.CharacterName, "Ilsa Varn").Add(x => x.Connected, true).Add(x => x.CanCopyRoom, true));
		cut.WaitForAssertion(() => cut.Find(".play-side-scene-copy"), TimeSpan.FromSeconds(5));
		var link = cut.Find(".play-side-scene-copy");
		await Assert.That(link.GetAttribute("href")).IsEqualTo("/scenes?start=copy");
		await Assert.That(link.TextContent.Trim()).IsEqualTo("Start a scene in a copy of this room");

		var without = RenderSidebar();
		without.WaitForAssertion(() => without.Find(".play-side-scene-empty"), TimeSpan.FromSeconds(5));
		await Assert.That(without.FindAll(".play-side-scene-copy").Count).IsEqualTo(0);
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
		await Assert.That(cut.FindAll(".play-side-empty, .kit-section-label, .play-side-browse").Count).IsEqualTo(0);
	}

	private static readonly CommChannel[] TwoChannels =
	[
		new("Public", 0, Joined: false, Members: 12, Description: "Chatter about anything"),
		new("Staff", 0, Joined: false, Members: 3),
	];

	/// <summary>
	/// The commands the browser sends, answering each with <paramref name="output"/> and, as the character's
	/// standing read straight after, <paramref name="standing"/>.
	/// </summary>
	private List<(string Command, string? Result)> CaptureCommands(string standing, params string[] output)
	{
		Services.AddSingleton<GameCommandService>();
		Services.AddSingleton<ChannelBrowserService>();
		var sent = new List<(string, string?)>();
		_api.Extra["POST /api/commands"] = System.Text.Json.JsonSerializer.Serialize(
			new { output, result = standing, truncated = false });
		_api.OnRequest = async request =>
		{
			if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/commands")
			{
				using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
				sent.Add((body.RootElement.GetProperty("command").GetString()!,
					body.RootElement.TryGetProperty("result", out var result) ? result.GetString() : null));
			}
		};
		return sent;
	}

	/// <summary>
	/// Browse channels opens the browser in place: every channel the comm.channels push lists, with its
	/// description, its member count and a switch, off for one the character is not on.
	/// </summary>
	[Test]
	public async Task BrowseChannels_ListsEveryChannelThePushLists_WithAJoinSwitch()
	{
		CaptureCommands("OFF");
		_feed.ChannelList = TwoChannels;
		var cut = RenderSidebar();
		await Assert.That(cut.FindAll(".play-side-channels").Count).IsEqualTo(0).Because("neither is joined");
		await cut.Find(".play-side-browse").ClickAsync();

		var rows = cut.FindAll(".chan-browse-row");
		await Assert.That(rows.Count).IsEqualTo(2);
		await Assert.That(rows[0].QuerySelector(".chan-browse-name")!.TextContent).IsEqualTo("#Public");
		await Assert.That(rows[0].QuerySelector(".chan-browse-desc")!.TextContent).IsEqualTo("Chatter about anything");
		await Assert.That(rows[0].QuerySelector(".chan-browse-count")!.TextContent).IsEqualTo("12 members");
		await Assert.That(rows[0].QuerySelector("[role=switch]")!.GetAttribute("aria-checked")).IsEqualTo("false");
		await Assert.That(rows[0].QuerySelector(".chan-browse-gag")).IsNull().Because("only a channel you are on can be gagged");
		await Assert.That(rows[1].QuerySelector(".chan-browse-desc")).IsNull();
		await Assert.That(cut.FindAll(".play-side-channels-empty").Count).IsEqualTo(0).Because("the browser says it instead");
	}

	/// <summary>The browser takes the place of the channel rows while it is open, and Done brings them back.</summary>
	[Test]
	public async Task BrowseChannels_ReplacesTheChannelRows_UntilDone()
	{
		CaptureCommands("OFF");
		_feed.ChannelList = [new CommChannel("Public", 0), new CommChannel("Newbie", 0, Joined: false)];
		var cut = RenderSidebar();
		await Assert.That(cut.FindAll(".play-side-channels .kit-row").Count).IsEqualTo(1);

		await cut.Find(".play-side-browse").ClickAsync();
		await Assert.That(cut.FindAll(".chan-browse-row").Count).IsEqualTo(2);
		await Assert.That(cut.FindAll(".play-side-channels").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".play-side-browse").GetAttribute("aria-expanded")).IsEqualTo("true");
		await Assert.That(cut.Find(".play-side-browse").TextContent.Trim()).IsEqualTo("Done");

		await cut.Find(".play-side-browse").ClickAsync();
		await Assert.That(cut.FindAll(".chan-browse").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".play-side-channels .kit-row").Count).IsEqualTo(1);
	}

	/// <summary>
	/// The switch runs @channel/on and reads the standing in the same queue entry; it shows the change at
	/// once, and keeps showing it once the push agrees.
	/// </summary>
	[Test]
	public async Task BrowseChannels_TheSwitchJoins_AndShowsItBeforeThePushArrives()
	{
		var sent = CaptureCommands("ON", "CHAT: You join channel <Public>.");
		_feed.ChannelList = TwoChannels;
		var cut = RenderSidebar();
		await cut.Find(".play-side-browse").ClickAsync();

		await cut.Find("[data-channel=Public] [role=switch]").ClickAsync();

		cut.WaitForAssertion(() => cut.Find("[data-channel=Public] .chan-browse-gag"), TimeSpan.FromSeconds(5));
		await Assert.That(sent).IsEquivalentTo([("@channel/on Public", (string?)"cstatus(%#,Public)")]);
		await Assert.That(cut.Find("[data-channel=Public] [role=switch]").GetAttribute("aria-checked")).IsEqualTo("true");
		await Assert.That(cut.FindAll(".chan-browse-error").Count).IsEqualTo(0);

		_feed.ChannelList = [TwoChannels[0] with { Joined = true }, TwoChannels[1]];
		await cut.InvokeAsync(_feed.Raise);
		await Assert.That(cut.Find("[data-channel=Public] [role=switch]").GetAttribute("aria-checked")).IsEqualTo("true");
	}

	[Test]
	public async Task BrowseChannels_TheBellGags_AndARefusalSaysWhy()
	{
		var sent = CaptureCommands("ON", "CHAT: You can't do that.");
		_feed.ChannelList = [new CommChannel("Public", 0, Members: 13)];
		var cut = RenderSidebar();
		await cut.Find(".play-side-browse").ClickAsync();

		await cut.Find(".chan-browse-gag").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".chan-browse-error"), TimeSpan.FromSeconds(5));
		await Assert.That(sent.Select(s => s.Command)).IsEquivalentTo(["@channel/gag Public=yes"]);
		await Assert.That(cut.Find(".chan-browse-error").TextContent).Contains("You can't do that.");
		await Assert.That(cut.Find(".chan-browse-gag").GetAttribute("aria-pressed")).IsEqualTo("false");
	}

	[Test]
	[Arguments("Public", "Public")]
	[Arguments("[a]%b=c", @"\[a\]\%b\=c")]
	public async Task AChannelName_GoesIntoTheCommand_Literally(string name, string expected)
		=> await Assert.That(ChannelBrowserService.Argument(name)).IsEqualTo(expected);

	[Test]
	[Arguments("OFF", false, false)]
	[Arguments("ON", true, false)]
	[Arguments("ON GAG HIDE", true, true)]
	[Arguments("#-1 NO SUCH CHANNEL", false, false)]
	public async Task TheStanding_IsReadFromCstatus(string result, bool joined, bool gagged)
		=> await Assert.That(ChannelBrowserService.ReadStanding(result)).IsEqualTo((joined, gagged));

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

	[Test]
	public async Task Groups_LeadTheChannels_WithTheirChannelsUnreadTogether_AndOpenByTheirKey()
	{
		_groupStore.Json = CommGroup.ToJson([new CommGroup("Roleplay", ["Public", "Scenes", "Staff"])]);
		_feed.ChannelList = [new CommChannel("Public", 3), new CommChannel("Scenes", 2), new CommChannel("Staff", 4, Joined: false), new CommChannel("OOC", 1)];
		string? opened = null;
		var cut = RenderSidebar(onOpen: key => opened = key);
		cut.WaitForAssertion(() => cut.Find(".play-side-group"), TimeSpan.FromSeconds(5));

		var labels = cut.FindAll(".play-side-channels .kit-row-label").Select(l => l.TextContent).ToList();
		await Assert.That(labels.First()).IsEqualTo("Roleplay");
		await Assert.That(cut.Find(".play-side-group").TextContent).Contains("5")
			.Because("Public's 3 and Scenes' 2; Staff is not joined, so its lines are not the character's");
		await cut.Find(".play-side-group").ClickAsync();
		await Assert.That(opened).IsEqualTo("group Roleplay");
	}

	/// <summary>Groups belong to the account: a character on no channel still sees and opens them.</summary>
	[Test]
	public async Task Groups_ShowWhenTheCharacterIsOnNoChannel()
	{
		_groupStore.Json = CommGroup.ToJson([new CommGroup("Roleplay", ["Public", "Scenes"])]);
		_feed.ChannelList = [];
		var cut = RenderSidebar();

		cut.WaitForAssertion(() => cut.Find(".play-side-group"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-side-group").TextContent).Contains("Roleplay");
	}

	/// <summary>The sidebar beside a dialog provider, which the group editor's dialog renders through.</summary>
	private IRenderedComponent<Bunit.Rendering.ContainerFragment> RenderSidebarWithDialogs() => Render(builder =>
	{
		builder.OpenComponent<MudBlazor.MudDialogProvider>(0);
		builder.CloseComponent();
		builder.OpenComponent<MudBlazor.MudPopoverProvider>(1);
		builder.CloseComponent();
		builder.OpenComponent<PlaySidebar>(2);
		builder.AddAttribute(3, nameof(PlaySidebar.Connected), true);
		builder.CloseComponent();
	});

	[Test]
	public async Task Browsing_OffersNewGroup_AndSavingOneListsIt()
	{
		_feed.ChannelList = [new CommChannel("Public", 0), new CommChannel("Scenes", 0), new CommChannel("OOC", 0)];
		var cut = RenderSidebarWithDialogs();
		await cut.Find(".play-side-browse").ClickAsync();
		await cut.Find(".play-side-group-new").ClickAsync();

		cut.WaitForAssertion(() => cut.Find("#play-group-name"), TimeSpan.FromSeconds(5));
		await cut.Find("#play-group-name").InputAsync("Roleplay");
		var boxes = cut.FindAll(".group-edit-channel input");
		await boxes[0].ChangeAsync(new ChangeEventArgs { Value = true });
		await cut.FindAll(".group-edit-channel input")[1].ChangeAsync(new ChangeEventArgs { Value = true });
		await cut.FindAll(".mud-dialog-actions button").Single(b => b.TextContent.Trim() == "Save").ClickAsync();

		cut.WaitForState(() => cut.FindAll(".play-side-group-edit-name").Count == 1, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-side-group-edit-name").TextContent).IsEqualTo("Roleplay");
		await Assert.That(CommGroup.FromJson(_groupStore.Json).Single().Channels)
			.IsEquivalentTo(new[] { "Public", "Scenes" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TheEditor_SaysWhyItRefused_AndStaysOpen()
	{
		_feed.ChannelList = [new CommChannel("Public", 0), new CommChannel("Scenes", 0)];
		var cut = RenderSidebarWithDialogs();
		await cut.Find(".play-side-browse").ClickAsync();
		await cut.Find(".play-side-group-new").ClickAsync();
		cut.WaitForAssertion(() => cut.Find("#play-group-name"), TimeSpan.FromSeconds(5));
		await cut.Find("#play-group-name").InputAsync("Solo");
		await cut.Find(".group-edit-channel input").ChangeAsync(new ChangeEventArgs { Value = true });
		await cut.FindAll(".mud-dialog-actions button").Single(b => b.TextContent.Trim() == "Save").ClickAsync();

		cut.WaitForState(() => cut.FindAll(".group-edit-error").Count == 1, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".group-edit-error").TextContent).IsEqualTo("Choose at least 2 channels.");
		await Assert.That(_groupStore.Json).IsNull();
	}
}

