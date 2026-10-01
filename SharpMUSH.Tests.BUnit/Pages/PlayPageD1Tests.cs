using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Components.Play;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Tests.BUnit.Components;
using PlayPage = SharpMUSH.Client.Pages.Play;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// README §5 /play composed (boards 01–11): the Play sidebar in the shell's slot, the room banner, the
/// scene card with Story | Terminal and focus mode, Here and Exits in the aside, the character sheet,
/// the composer, and Play's own tabs and Room sheet for the narrow tier. Quick actions are gone.
/// </summary>
public class PlayPageD1Tests : TrackingBunitContext
{
	private readonly OobChannelStore _store = new();
	private readonly TestCommFeed _comms = new();

	/// <summary>A game panel shipped for the Play scope (board 13), fed by an OOB package.</summary>
	private static readonly SharpMUSH.Client.Models.Applications.PortalApplication[] Apps =
	[
		new("weather", "Weather", null, "Widget", "http/weather/schema", null, null, "Guest", null, ["RightSidebar"], 30,
			Scope: "play", OobPackage: "weather.now"),
		// Staff-only: never offered to a visitor below its minimum role.
		new("staffboard", "Staff board", null, "Widget", "http/staff/schema", null, null, "Wizard", null, ["RightSidebar"], 40,
			Scope: "play"),
		// Declares it runs only in the main content zone: never appended to the aside.
		new("ledger", "Ledger", null, "Widget", "http/ledger/schema", null, null, "Guest", null, ["MainContent"], 50,
			Scope: "play"),
	];
	private readonly IPlayTerminalService _play = Substitute.For<IPlayTerminalService>();
	private readonly FakeSceneHub _hub;

	public PlayPageD1Tests()
	{
		Services.AddMudServices();
		Services.AddLocalization();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true));
		JSInterop.Mode = JSRuntimeMode.Loose;

		_play.OobChannels.Returns(_store);
		_play.IsConnected.Returns(true);
		_play.ConnectedPlayerName.Returns("Ilsa Varn");
		_play.Lines.Returns(Array.Empty<SharpMUSH.Client.Models.TerminalLine>());
		Services.AddSingleton(_play);

		var terminal = Substitute.For<ITerminalService>();
		terminal.OobChannels.Returns(new OobChannelStore());
		terminal.Lines.Returns(Array.Empty<SharpMUSH.Client.Models.TerminalLine>());
		Services.AddSingleton(terminal);

		var client = Track(new HttpClient(new PlayApi()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(client);
		Services.AddSingleton(factory);
		Services.AddSingleton(sp => new AccountAuthService(factory, sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
			NullLogger<AccountAuthService>.Instance, []));

		var hostEnv = Substitute.For<IWebAssemblyHostEnvironment>();
		hostEnv.Environment.Returns("Production");
		Services.AddSingleton(hostEnv);

		_hub = PlayPageServices.Install(Services, _comms, Apps);
	}

	/// <summary>The shell's page-sidebar outlet and a popover provider beside the page, as MainLayout composes them.</summary>
	private sealed class Host : ComponentBase
	{
		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			builder.OpenComponent<MudPopoverProvider>(0);
			builder.CloseComponent();
			builder.OpenComponent<PageSidebarHost>(1);
			builder.AddAttribute(2, nameof(PageSidebarHost.ChildContent), (RenderFragment)(b =>
			{
				b.OpenComponent<PlayPage>(0);
				b.CloseComponent();
			}));
			builder.CloseComponent();
		}
	}

	private IRenderedComponent<Host> RenderPlay()
	{
		var cut = Render<Host>();
		cut.WaitForAssertion(() => cut.Find(".play"), TimeSpan.FromSeconds(5));
		return cut;
	}

	private void PushRoom(bool scene = true)
	{
		var sceneJson = scene ? ""","scene":{"id":"42","title":"Salt Market at Dusk","cast":5}""" : string.Empty;
		_store.Set(OobEntryParser.RoomInfoPackage,
			$$"""{"v":2,"dbref":"#1201","objid":"#1201:1","name":"Lower Docks","area":"Harbour Ward","image":{"url":"/r/docks.jpg"}{{sceneJson}}}""");
		_store.Set(OobEntryParser.RoomContentsPackage,
			"""{"v":2,"who":[{"dbref":"#312","objid":"#312:1","type":"player","name":"Tomas Reyes","cmd":"look #312","profile":true,"status":"active"},{"dbref":"#313","objid":"#313:1","type":"player","name":"Ilsa Varn","cmd":"look #313","you":true}]}""");
		_store.Set(OobEntryParser.RoomExitsPackage,
			"""{"v":2,"exits":[{"dbref":"#1210","name":"Harbour Row","aliases":["n"],"cmd":"goto #1210","state":"open"}]}""");
	}

	[Test]
	public async Task TheSidebar_GoesIntoTheShellsSlot()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".test-pagebar .play-side-scene"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".test-pagebar .kit-side-sub").TextContent).Contains("Connected as Ilsa Varn");
	}

	/// <summary>
	/// A game with the Scene System off shows no In scene row and no story view, even if the room still
	/// reports a scene from before it was turned off: there would be nothing behind either.
	/// </summary>
	[Test]
	public async Task WithoutTheSceneSystem_TheRoomsSceneIsNotOffered()
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(true, features: []));
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .exit"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".play-side-scene")).IsEmpty();
		await Assert.That(cut.Markup).DoesNotContain("Salt Market at Dusk");
	}

	[Test]
	public async Task PushedRooms_DrawTheBanner_HereAndExits_AndAnExitGoes()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .exit"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".kit-banner-title").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.Find(".play-aside .here-card .kit-card-title").TextContent).IsEqualTo("Here · 1");
		cut.Find(".play-aside .exit button.exit-go").Click();
		await _play.Received(1).SendAsync("goto #1210");
		await Assert.That(cut.Markup).DoesNotContain("QuickActions").Because("§5.6: Quick actions is dropped");
	}

	[Test]
	public async Task InAScene_StoryIsTheDefault_WithTheComposer_AndTheTerminalStaysMounted()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find("[role='radiogroup'][aria-label='View']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-story").ClassList).DoesNotContain("play-view--off");
		await Assert.That(cut.Find(".play-terminal").HasAttribute("inert")).IsTrue();
		await Assert.That(cut.FindAll(".composer").Count).IsEqualTo(1);
		await Assert.That(cut.FindComponents<GlobalTerminal>().Count).IsEqualTo(1);

		cut.FindAll(".scene-card-radio")[1].Click();
		await Assert.That(cut.Find(".play-story").ClassList).Contains("play-view--off");
		await Assert.That(cut.Find(".play-terminal").HasAttribute("inert")).IsFalse();
		await Assert.That(cut.FindAll(".composer").Count).IsEqualTo(0).Because("the terminal keeps its own input");
		await Assert.That(cut.FindComponents<GlobalTerminal>().Count).IsEqualTo(1);
	}

	[Test]
	public async Task OutsideAScene_ThereIsOnlyTheTerminal()
	{
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".kit-banner-title"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("[role='radiogroup'][aria-label='View']").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".play-story").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".scene-card-title").TextContent).IsEqualTo("Lower Docks");
		await Assert.That(cut.FindAll(".composer").Count).IsEqualTo(0);
	}

	[Test]
	public async Task FocusMode_StepsTheSidebarBannerAndAsideAway()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside"), TimeSpan.FromSeconds(5));
		cut.Find("button.scene-card-focus").Click();
		await Assert.That(cut.FindAll(".test-pagebar .kit-pagebar").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".play-aside").HasAttribute("hidden")).IsTrue()
			.Because("the aside stays mounted, so its exit keys keep working in focus mode");
		await Assert.That(cut.FindComponents<ExitsCard>().Count).IsEqualTo(1);
		await Assert.That(cut.FindComponents<GlobalTerminal>().Count).IsEqualTo(1);
		cut.Find("button.scene-card-focus").Click();
		await Assert.That(cut.Find(".play-aside").HasAttribute("hidden")).IsFalse();
	}

	[Test]
	public async Task TheComposer_SendsThroughThePlayConnection()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".composer textarea"), TimeSpan.FromSeconds(5));
		cut.Find(".composer textarea").Input("leans on the crates");
		cut.Find("button.composer-send").Click();
		await _play.Received(1).SendAsync("pose leans on the crates");
	}

	[Test]
	public async Task ARefusedLiveSubscription_WarnsInTheStory_AndTheComposerDoesNotSend()
	{
		_hub.JoinRefusal = new Microsoft.AspNetCore.SignalR.HubException("no character can see this scene");
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-story .play-story-unavailable"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-story-unavailable").GetAttribute("role")).IsEqualTo("status");

		cut.Find(".composer textarea").Input("leans on the crates");
		await Assert.That(cut.Find("button.composer-send").HasAttribute("disabled")).IsTrue()
			.Because("this client would never see the pose arrive in the story");
		cut.Find("button.composer-send").Click();
		await _play.DidNotReceive().SendAsync(Arg.Any<string>());
	}

	[Test]
	public async Task ACharacterInHere_OpensTheSheet_AndPageStartsAPageInTheComposer()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .kit-portrait"), TimeSpan.FromSeconds(5));
		cut.Find(".play-aside .kit-portrait").Click();
		cut.WaitForAssertion(() => cut.Find(".sheet[role='dialog']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".sheet-name").TextContent).IsEqualTo("Tomas Reyes");
		cut.FindAll(".sheet-actions > *")[1].Click();
		await Assert.That(cut.FindAll(".sheet").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".composer textarea").GetAttribute("value")).IsEqualTo("page Tomas Reyes=");
	}

	[Test]
	public async Task TheRoomTab_OpensTheRoomSheet_WhoseExitsAreRowsThatGo()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-tabs"), TimeSpan.FromSeconds(5));
		var tabs = cut.FindAll(".play-tab");
		await Assert.That(tabs.Select(t => t.TextContent.Trim()).ToList())
			.IsEquivalentTo(new[] { "Scene", "Room", "#Channels", "Pages" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		tabs[1].Click();
		var sheet = cut.Find(".play-sheet[role='dialog']");
		await Assert.That(sheet.GetAttribute("aria-modal")).IsEqualTo("true");
		await Assert.That(cut.Find(".mud-overlay").GetAttribute("style")).Contains("align-items: flex-end")
			.Because("MudOverlay centres a zero-size content box; the sheet sits on the bottom edge instead");
		var sheetTabs = cut.FindAll(".play-sheet [role='tab']").Select(t => t.TextContent.Trim()).ToList();
		await Assert.That(sheetTabs).IsEquivalentTo(new[] { "Here · 1", "Exits · 1", "Weather" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("the Room sheet is the play layout, then the scope panels, as the desktop aside is");
		await Assert.That(cut.FindAll(".play-sheet .exits--rows .exit").Count).IsEqualTo(1);
		cut.Find(".play-sheet .exit button.exit-go").Click();
		await _play.Received(1).SendAsync("goto #1210");
		await Assert.That(cut.FindAll(".play-sheet").Count).IsEqualTo(0);
	}

	[Test]
	public async Task APanelAboveTheViewersRole_IsNotShown()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .play-panel"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-aside .play-panel .kit-card-title").Select(t => t.TextContent).ToList())
			.IsEquivalentTo(new[] { "Weather" });
	}

	[Test]
	public async Task PlacingAPanelInTheLayout_WhilePlayIsOpen_DoesNotShowItTwice()
	{
		var layouts = Substitute.For<ILayoutService>();
		var withoutWeather = PlayLayout("Here", "Exits");
		var withWeather = PlayLayout("Here", "Exits", "weather");
		var current = withoutWeather;
		layouts.GetLayoutAsync(Arg.Any<string>()).Returns(_ => Task.FromResult(current));
		Services.AddSingleton(layouts);
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .play-panel"), TimeSpan.FromSeconds(5));

		current = withWeather;
		layouts.OnLayoutChanged += Raise.Event<Action<string>>(LayoutScopes.Play);
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".play-aside .play-panel").Count != 0) throw new InvalidOperationException("the panel is still listed apart");
		}, TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task APanelAboveTheViewersRole_StaysHidden_WhenTheLayoutPlacesIt()
	{
		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(Arg.Any<string>()).Returns(Task.FromResult(PlayLayout("Here", "Exits", "staffboard")));
		Services.AddSingleton(layouts);
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .play-panel"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindComponents<SharpMUSH.Client.Components.Widgets.SchemaWidget>()
				.Select(w => w.Instance.WidgetName).ToList())
			.DoesNotContain("staffboard").Because("placing a Wizard panel in the layout does not lower its minimum role");
		cut.FindAll(".play-tab")[1].Click();
		await Assert.That(cut.FindAll(".play-sheet [role='tab']").Select(t => t.TextContent.Trim()).ToList())
			.IsEquivalentTo(new[] { "Here · 1", "Exits · 1", "Weather" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// Before the catalog is in (or when it failed), a placed application is the registry's by-slug fallback,
	/// which carries no role. The aside fetches it and checks the role before showing it, as zones do.
	/// </summary>
	private IRenderedComponent<Host> RenderPlayBeforeTheCatalog(params string[] placed)
	{
		Services.AddSingleton(new ApplicationCatalog([]));
		var registry = new WidgetRegistry();
		foreach (var widget in SharpMUSH.Client.Widgets.BuiltInWidgets.All) registry.Register(widget);
		Services.AddSingleton<IWidgetRegistry>(registry);
		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(Arg.Any<string>()).Returns(Task.FromResult(PlayLayout(placed)));
		Services.AddSingleton(layouts);
		var cut = RenderPlay();
		PushRoom();
		return cut;
	}

	private static List<string> AsideApps(IRenderedComponent<Host> cut) =>
		cut.FindComponents<SharpMUSH.Client.Components.Widgets.SchemaWidget>().Select(w => w.Instance.WidgetName).ToList();

	[Test]
	public async Task BeforeTheCatalogIsIn_APlacedPanelAboveTheViewersRole_IsNotShown()
	{
		var cut = RenderPlayBeforeTheCatalog("Here", "Exits", "staffboard");
		cut.WaitForState(() => cut.FindComponents<SharpMUSH.Client.Components.Layout.WidgetErrorBoundary>().Count >= 2, TimeSpan.FromSeconds(5));

		await Assert.That(AsideApps(cut)).DoesNotContain("staffboard");
		cut.FindAll(".play-tab")[1].Click();
		await Assert.That(cut.FindAll(".play-sheet [role='tab']").Select(t => t.TextContent.Trim()).ToList())
			.IsEquivalentTo(new[] { "Here · 1", "Exits · 1" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task BeforeTheCatalogIsIn_APlacedPanelTheViewerMayUse_IsShown()
	{
		AddAuthorization().SetAuthorized("headwiz").SetRoles("Wizard");
		var cut = RenderPlayBeforeTheCatalog("Here", "Exits", "staffboard");

		cut.WaitForState(() => AsideApps(cut).Contains("staffboard"), TimeSpan.FromSeconds(5));
		await Assert.That(AsideApps(cut)).Contains("staffboard");
	}

	[Test]
	public async Task APanelThatDoesNotAllowTheRightSidebar_IsNotAppended()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .play-panel"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-aside .play-panel .kit-card-title").Select(t => t.TextContent).ToList())
			.DoesNotContain("Ledger").Because("the ledger declares MainContent only");
	}

	private static LayoutConfiguration PlayLayout(params string[] names) =>
		new(new Dictionary<WidgetZone, List<WidgetPlacement>>
		{
			[WidgetZone.RightSidebar] = names.Select((n, i) => new WidgetPlacement(n, i, null)).ToList(),
		}, new LayoutSettings(false, false));

	[Test]
	public async Task TheInSceneRow_IsCurrentOnlyWhileTheStoryIsShown()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".test-pagebar .play-side-scene .kit-row"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".test-pagebar .play-side-scene .kit-row").GetAttribute("aria-current")).IsEqualTo("page");
		cut.FindAll(".scene-card-radio")[1].Click();
		await Assert.That(cut.Find(".test-pagebar .play-side-scene .kit-row").GetAttribute("aria-current")).IsNull();
	}

	[Test]
	public async Task PageFromTheSheet_UsesTheRowsPageAction_WhichNamesTheDbref()
	{
		var cut = RenderPlay();
		_store.Set(OobEntryParser.RoomInfoPackage, """{"v":2,"name":"Lower Docks","scene":{"id":"42","title":"Salt Market"}}""");
		_store.Set(OobEntryParser.RoomContentsPackage,
			"""{"v":2,"who":[{"dbref":"#312","type":"player","name":"Tomas Reyes","cmd":"look #312","profile":true,"actions":[{"label":"Page","cmd":"page #312="}]}]}""");
		cut.WaitForAssertion(() => cut.Find(".play-aside .kit-portrait"), TimeSpan.FromSeconds(5));
		cut.Find(".play-aside .kit-portrait").Click();
		cut.WaitForAssertion(() => cut.Find(".sheet"), TimeSpan.FromSeconds(5));
		cut.FindAll(".sheet-actions > *")[1].Click();
		await Assert.That(cut.Find(".composer textarea").GetAttribute("value")).IsEqualTo("page #312=");
	}

	[Test]
	public async Task AChannelRow_OpensTheChannelViewInMain_AndCloseReturnsToTheScene()
	{
		_comms.ChannelList = [new CommChannel("Public", 1)];
		_comms.Lines["Public"] = [new CommMessage("channel", "Public", [], "Wren Halloway", null, "anyone up for a scene?", DateTimeOffset.Now)];
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".test-pagebar .play-side-channels .kit-row"), TimeSpan.FromSeconds(5));
		cut.Find(".test-pagebar .play-side-channels .kit-row").Click();

		await Assert.That(cut.Find(".comm-title").TextContent).IsEqualTo("# Public");
		await Assert.That(cut.Find(".play-card").HasAttribute("hidden")).IsTrue();
		await Assert.That(cut.FindComponents<GlobalTerminal>().Count).IsEqualTo(1).Because("the terminal keeps its connection and output");
		await Assert.That(cut.Find(".test-pagebar .play-side-channels .kit-row").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.Find(".test-pagebar .play-side-scene .kit-row").GetAttribute("aria-current")).IsNull();

		cut.Find(".comm-compose input").Input("I'm in");
		cut.Find(".comm-compose button.comm-send").Click();
		await _play.Received(1).SendAsync("@chat Public=I'm in");

		cut.Find("button.comm-close").Click();
		await Assert.That(cut.FindAll(".comm").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".play-card").HasAttribute("hidden")).IsFalse();
		await Assert.That(_comms.Viewing).IsNull();
	}

	[Test]
	public async Task TheAside_IsThePlayLayout_ThenTheGamesPlayPanels()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .play-panel"), TimeSpan.FromSeconds(5));
		var order = cut.FindAll(".play-aside .kit-card-title").Select(t => t.TextContent).ToList();
		await Assert.That(order).IsEquivalentTo(new[] { "Here · 1", "Exits · 1", "Weather" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(cut.FindComponents<SharpMUSH.Client.Components.Widgets.HereWidget>().Count).IsEqualTo(1)
			.Because("Here and Exits come from the play layout scope, not markup in the page");
	}

	[Test]
	public async Task MinimisingTheBanner_IsRemembered()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find("button.kit-banner-minimise"), TimeSpan.FromSeconds(5));
		cut.Find("button.kit-banner-minimise").Click();
		await Assert.That(cut.FindAll(".kit-banner-strip").Count).IsEqualTo(1);
		var stored = JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem").Select(i => i.Arguments).ToList();
		await Assert.That(stored.Any(a => (string?)a[0] == "play.banner" && (string?)a[1] == "min")).IsTrue();
	}

	private sealed class PlayApi : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			var body = request.RequestUri!.AbsolutePath switch
			{
				"/api/scenes/42/poses" => "[]",
				"/http/characters" => """[{"name":"Tomas Reyes","objid":"#312:1","created":1,"category":""}]""",
				"/api/profile/Tomas%20Reyes/gallery" => "[]",
				// The per-slug fetch a placement falls back to while the catalog is empty (pending or failed).
				"/api/applications/staffboard" => System.Text.Json.JsonSerializer.Serialize(Apps[1], System.Text.Json.JsonSerializerOptions.Web),
				_ => null,
			};
			return Task.FromResult(body is null
				? new HttpResponseMessage(HttpStatusCode.NotFound)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
		}
	}
}
