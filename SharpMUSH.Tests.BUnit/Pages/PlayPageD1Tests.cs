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
		/// <summary>MainLayout's drawer handle, cascaded to the page when set.</summary>
		[Parameter] public ShellNavigation? Shell { get; set; }

		protected override void BuildRenderTree(RenderTreeBuilder builder)
		{
			builder.OpenComponent<MudPopoverProvider>(0);
			builder.CloseComponent();
			builder.OpenComponent<PageSidebarHost>(1);
			builder.AddAttribute(2, nameof(PageSidebarHost.ChildContent), (RenderFragment)(b =>
			{
				if (Shell is null)
				{
					b.OpenComponent<PlayPage>(0);
					b.CloseComponent();
					return;
				}
				b.OpenComponent<CascadingValue<ShellNavigation>>(1);
				b.AddAttribute(2, "Value", Shell);
				b.AddAttribute(3, "IsFixed", true);
				b.AddAttribute(4, "ChildContent", (RenderFragment)(c =>
				{
					c.OpenComponent<PlayPage>(0);
					c.CloseComponent();
				}));
				b.CloseComponent();
			}));
			builder.CloseComponent();
		}
	}

	private IRenderedComponent<Host> RenderPlay(ShellNavigation? shell = null)
	{
		var cut = Render<Host>(p => p.Add(h => h.Shell, shell));
		cut.WaitForAssertion(() => cut.Find(".play"), TimeSpan.FromSeconds(5));
		return cut;
	}

	private void PushRoom(bool scene = true, string sceneId = "42", bool? focus = null, bool elsewhere = false)
	{
		var focusJson = focus is { } f
			? $$""","role":"participant","focus":{{(f ? "true" : "false")}},"elsewhere":{{(elsewhere ? "true" : "false")}}"""
			: string.Empty;
		var sceneJson = scene ? $$""","scene":{"id":"{{sceneId}}","title":"Salt Market at Dusk","cast":5{{focusJson}}}""" : string.Empty;
		_store.Set(OobEntryParser.RoomInfoPackage,
			$$"""{"v":2,"dbref":"#1201","objid":"#1201:1","name":"Lower Docks","area":"Harbour Ward","image":{"url":"/r/docks.jpg"}{{sceneJson}}}""");
		_store.Set(OobEntryParser.RoomContentsPackage,
			"""{"v":2,"who":[{"dbref":"#312","objid":"#312:1","type":"player","name":"Tomas Reyes","cmd":"look #312","profile":true,"status":"active"},{"dbref":"#313","objid":"#313:1","type":"player","name":"Ilsa Varn","cmd":"look #313","you":true}]}""");
		_store.Set(OobEntryParser.RoomExitsPackage,
			"""{"v":2,"exits":[{"dbref":"#1210","name":"Harbour Row","aliases":["n"],"cmd":"goto #1210","state":"open"}]}""");
	}

	/// <summary>Opens the room scene's Story from the marker above the terminal, as a player would.</summary>
	private static async Task OpenStory(IRenderedComponent<Host> cut)
	{
		cut.WaitForAssertion(() => cut.Find("button.play-scene-hint-open"), TimeSpan.FromSeconds(5));
		await cut.Find("button.play-scene-hint-open").ClickAsync();
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
		JSInterop.Setup<bool>("sharpmushLayout.isTouchChrome").SetResult(true);
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .exit"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".play-side-scene")).IsEmpty();
		await Assert.That(cut.Markup).DoesNotContain("Salt Market at Dusk");
		await Assert.That(cut.FindAll("[role='radiogroup'], .scene-card-viewtoggle")).IsEmpty()
			.Because("with no scene to show there is only the terminal");

		// The menu button's panel draws the sidebar again, with the same room.
		cut.WaitForAssertion(() => cut.Find("button.play-menu-btn"), TimeSpan.FromSeconds(5));
		await cut.Find("button.play-menu-btn").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".play-sheet-body"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-side-scene")).IsEmpty();
	}

	[Test]
	public async Task PushedRooms_DrawTheHeader_HereAndExits_AndAnExitGoes()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside .exit"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(0).Because("the banner is folded into the card header on every screen");
		await Assert.That(cut.Find(".scene-card-head-img").GetAttribute("src")).EndsWith("/r/docks.jpg");
		await Assert.That(cut.Find(".play-aside .here-card .kit-card-title").TextContent).IsEqualTo("Here · 1");
		await cut.Find(".play-aside .exit button.exit-go").ClickAsync();
		await _play.Received(1).SendAsync("goto #1210");
		await Assert.That(cut.Markup).DoesNotContain("QuickActions").Because("§5.6: Quick actions is dropped");
	}

	/// <summary>
	/// Walking into a room with a scene keeps the terminal: a marker above it says a scene is running and
	/// opens the Story when pressed. The Story's composer replaces the terminal's line, which stays mounted.
	/// </summary>
	[Test]
	public async Task InAScene_TheTerminalStays_AndTheMarkerOpensTheStory()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-scene-hint"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-story").ClassList).Contains("play-view--off");
		await Assert.That(cut.Find(".play-terminal").HasAttribute("inert")).IsFalse();
		await Assert.That(cut.Find(".play-scene-hint-text").TextContent).Contains("A scene is running here");
		await Assert.That(cut.Find(".play-scene-hint-title").TextContent).IsEqualTo("Salt Market at Dusk");
		await Assert.That(cut.FindAll(".composer").Count).IsEqualTo(0).Because("the terminal keeps its own input");
		await Assert.That(cut.FindAll(".scene-card-foot").Count).IsEqualTo(0)
			.Because("no empty footer either: it pads itself, and a sideways phone has no height to give it (#1506)");

		await OpenStory(cut);
		await Assert.That(cut.Find(".play-story").ClassList).DoesNotContain("play-view--off");
		await Assert.That(cut.Find(".play-terminal").HasAttribute("inert")).IsTrue();
		await Assert.That(cut.FindAll(".composer").Count).IsEqualTo(1);
		await Assert.That(cut.FindComponents<GlobalTerminal>().Count).IsEqualTo(1);

		await cut.FindAll(".scene-card-radio")[1].ClickAsync();
		await Assert.That(cut.Find(".play-story").ClassList).Contains("play-view--off");
		await Assert.That(cut.FindAll(".composer").Count).IsEqualTo(0);
		await Assert.That(cut.FindComponents<GlobalTerminal>().Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(".play-scene-hint").Count).IsEqualTo(0)
			.Because("a player who chose the terminal in this scene knows it is here");
	}

	/// <summary>The marker's close button hides it for that scene; the next scene is marked again.</summary>
	[Test]
	public async Task TheMarker_HidesForItsScene_AndComesBackForTheNext()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-scene-hint"), TimeSpan.FromSeconds(5));
		await cut.Find("button.play-scene-hint-hide").ClickAsync();
		await Assert.That(cut.FindAll(".play-scene-hint").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".play-story").ClassList).Contains("play-view--off")
			.Because("hiding the marker does not open the scene");

		PushRoom(sceneId: "43");
		cut.WaitForAssertion(() => cut.Find(".play-scene-hint"), TimeSpan.FromSeconds(5));
	}

	/// <summary>
	/// A viewer not focused on the room's scene would pose into the room and never into the story, so the
	/// Story's footer says so and offers to join instead of the composer.
	/// </summary>
	[Test]
	public async Task NotFocusedOnTheScene_TheStoryOffersToJoin_InsteadOfTheComposer()
	{
		var cut = RenderPlay();
		PushRoom(focus: false);
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".play-join"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".composer").Count).IsEqualTo(0);

		await cut.Find("button.play-join-btn").ClickAsync();
		await _play.Received(1).SendAsync("+scene/join 42");
	}

	/// <summary>
	/// The join bar says why a pose would miss the story: the viewer has not joined it, or their poses go to
	/// the scene they are focused on instead.
	/// </summary>
	[Test]
	[Arguments(false, "You haven't joined this scene yet. Join it to pose here.")]
	[Arguments(true, "Your poses go to another scene. Join this one to pose here.")]
	public async Task TheJoinBar_SaysWhetherTheViewerIsInAnotherScene(bool elsewhere, string text)
	{
		var cut = RenderPlay();
		PushRoom(focus: false, elsewhere: elsewhere);
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".play-join"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".play-join-text").TextContent).IsEqualTo(text);
	}

	/// <summary>
	/// A refused join prints its reason to the terminal, which the Story view does not show: the join bar
	/// repeats what the game said, and says the join failed when the game said nothing.
	/// </summary>
	[Test]
	[Arguments("You are not approved to take part in scenes.", "You are not approved to take part in scenes.")]
	[Arguments(null, "The game did not let you join this scene.")]
	public async Task ARefusedJoin_SaysWhyInTheJoinBar(string? said, string shown)
	{
		_play.When(p => p.SendAsync("+scene/join 42")).Do(_ =>
		{
			if (said is not null)
				_play.LineReceived += Raise.Event<Action<SharpMUSH.Client.Models.TerminalLine>>(
					new SharpMUSH.Client.Models.TerminalLine(DateTime.UtcNow, said, SharpMUSH.Client.Models.TerminalLineSource.Server));
		});
		_play.SendCommandAsync("scenefocus(me)", Arg.Any<int>()).Returns(["#-1 NOT FOUND"]);

		var cut = RenderPlay();
		PushRoom(focus: false);
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".play-join"), TimeSpan.FromSeconds(5));
		await cut.Find("button.play-join-btn").ClickAsync();

		cut.WaitForAssertion(() => cut.Find(".play-join-refusal"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-join-refusal").TextContent).IsEqualTo(shown);
	}

	/// <summary>A join that took leaves nothing to say: the room.info that follows brings the composer.</summary>
	[Test]
	public async Task AJoinThatTook_ShowsNoRefusal()
	{
		_play.SendCommandAsync("scenefocus(me)", Arg.Any<int>()).Returns(["42"]);

		var cut = RenderPlay();
		PushRoom(focus: false);
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".play-join"), TimeSpan.FromSeconds(5));
		await cut.Find("button.play-join-btn").ClickAsync();

		await _play.Received(1).SendCommandAsync("scenefocus(me)", Arg.Any<int>());
		await Assert.That(cut.FindAll(".play-join-refusal").Count).IsEqualTo(0);
	}

	/// <summary>The room.info that follows a join brings the composer back, with a Leave beside it.</summary>
	[Test]
	public async Task OnceFocused_TheComposerIsBack_AndLeaveLeavesTheScene()
	{
		var cut = RenderPlay();
		PushRoom(focus: false);
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".play-join"), TimeSpan.FromSeconds(5));

		PushRoom(focus: true);
		cut.WaitForAssertion(() => cut.Find(".composer"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-join").Count).IsEqualTo(0);

		await cut.Find("button[aria-label='Leave scene']").ClickAsync();
		await _play.Received(1).SendAsync("+scene/leave");
	}

	/// <summary>
	/// The page follows the room's scene as it changes: a scene that stops leaves the terminal, and a scene
	/// that starts later is only marked, even if the player had the last one's Story open.
	/// </summary>
	[Test]
	public async Task TheSceneEnding_LeavesTheTerminal_AndANewSceneIsOnlyMarked()
	{
		var cut = RenderPlay();
		PushRoom();
		await OpenStory(cut);
		await Assert.That(cut.Find(".play-story").ClassList).DoesNotContain("play-view--off");

		PushRoom(scene: false);
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".play-story").Count != 0) throw new InvalidOperationException("story still shown");
		}, TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-terminal").HasAttribute("inert")).IsFalse();
		await Assert.That(cut.FindAll(".play-scene-hint").Count).IsEqualTo(0);

		PushRoom(sceneId: "43");
		cut.WaitForAssertion(() => cut.Find(".play-scene-hint"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-story").ClassList).Contains("play-view--off");
	}

	[Test]
	public async Task OutsideAScene_ThereIsOnlyTheTerminal()
	{
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".scene-card-sub-text"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("[role='radiogroup'][aria-label='View']").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".play-story").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".composer").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheCardHeader_NamesTheCharacter_ThenTheRoom()
	{
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".scene-card-sub-text"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-card-title").TextContent).IsEqualTo("Ilsa Varn");
		await Assert.That(cut.Find(".scene-card-sub-text").TextContent).IsEqualTo("Lower Docks").Because("the folded banner's room is named in the header");
		await Assert.That(cut.Find(".play-me-initial").TextContent).IsEqualTo("IV").Because("she has no picture in the room's contents or the directory");
		await Assert.That(cut.Find(".play-me").ClassList).Contains("play-me--on");
		await Assert.That(cut.Find(".play-me-status").TextContent).IsEqualTo("Connected");

		await cut.Find("button.scene-card-focus").ClickAsync();
		await Assert.That(cut.Find(".scene-card-sub").TextContent).IsEqualTo("Lower Docks").Because("with the banner gone, the card names the room");
	}

	[Test]
	public async Task InAScene_TheSubLineIsTheScene()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".scene-card-sub-text"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-card-sub-text").TextContent).IsEqualTo("Salt Market at Dusk");
	}

	[Test]
	public async Task AGuest_IsNamedByTheirRowInTheRoomsContents()
	{
		// The server picks a guest's character, so the terminal never learns the name it connected as.
		_play.ConnectedPlayerName.Returns((string?)null);
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".play-aside .exit"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-card-title").TextContent).IsEqualTo("Ilsa Varn");
		await Assert.That(cut.Find(".play-me-initial").TextContent).IsEqualTo("IV");
	}

	[Test]
	public async Task OnTouchChrome_TheMenuButtonOpensPlaysSidebarFromTheLeft_WithTheSiteMenuAtItsTop()
	{
		// A chat app's layout: the card header leads with a menu button; it opens the channel list (Play's
		// sidebar), and the site's own menu is the first row there.
		JSInterop.Setup<bool>("sharpmushLayout.isTouchChrome").SetResult(true);
		var opened = 0;
		var cut = RenderPlay(new ShellNavigation(() => opened++));
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find("button.play-menu-btn"), TimeSpan.FromSeconds(5));
		var lead = cut.Find(".scene-card-lead .play-lead");
		await Assert.That(lead.FirstElementChild!.ClassList).Contains("play-menu-btn").Because("the menu comes first, then the avatar");
		await Assert.That(cut.Find("button.play-menu-btn").GetAttribute("aria-label")).IsEqualTo("Play sections");

		await cut.Find("button.play-menu-btn").ClickAsync();
		var sheet = cut.Find(".play-sheet[role='dialog']");
		await Assert.That(sheet.ClassList).Contains("play-sheet--side");
		await Assert.That(cut.Find(".mud-overlay").GetAttribute("style")).Contains("justify-content: flex-start");
		await Assert.That(cut.Find(".play-sheet .kit-side-sub").TextContent).Contains("Connected as Ilsa Varn").Because("Play's sidebar is the panel");

		await cut.Find("button.play-sheet-site").ClickAsync();
		await Assert.That(opened).IsEqualTo(1);
		await Assert.That(cut.FindAll(".play-sheet").Count).IsEqualTo(0).Because("the site menu replaces the panel");
	}

	[Test]
	public async Task UnreadChannelsOrPages_PutADotOnTheMenuButton()
	{
		JSInterop.Setup<bool>("sharpmushLayout.isTouchChrome").SetResult(true);
		_comms.ChannelList = [new CommChannel("Public", 2), new CommChannel("Staff", 5, Joined: false)];
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find("button.play-menu-btn .play-menu-dot"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("button.play-menu-btn .play-me-status").TextContent).IsEqualTo("2 unread")
			.Because("a channel the viewer has not joined does not count");
	}

	[Test]
	public async Task TheTerminalSettingsMenu_CarriesFocusMode()
	{
		// A phone's header folds the focus button into this menu (Play.razor.css), so the menu must reach it.
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find("button.play-settings-btn"), TimeSpan.FromSeconds(5));
		await cut.Find("button.play-settings-btn").ClickAsync();
		cut.WaitForAssertion(() => cut.Find("button.play-cfg-focus"), TimeSpan.FromSeconds(5));
		await cut.Find("button.play-cfg-focus").ClickAsync();
		// The page re-renders after the click's handler, not inside it: wait for that render.
		cut.WaitForAssertion(() => cut.Find(".play--focus"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play--focus").Count).IsEqualTo(1);
	}

	[Test]
	public async Task TheHeadersAndTheBannersSettingsMenus_OpenOnTheirOwn()
	{
		// The header and the banner's controls row each carry the menu; opening one must not open the other too.
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find("button.scene-card-sub--action"), TimeSpan.FromSeconds(5));
		await cut.Find("button.scene-card-sub--action").ClickAsync();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll("button.play-settings-btn").Count < 2) throw new InvalidOperationException("one menu so far");
		}, TimeSpan.FromSeconds(5));
		await cut.FindAll("button.play-settings-btn")[0].ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".play-cfg"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-cfg").Count).IsEqualTo(1);
	}

	[Test]
	public async Task NothingUnread_NoDot()
	{
		JSInterop.Setup<bool>("sharpmushLayout.isTouchChrome").SetResult(true);
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find("button.play-menu-btn"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-menu-dot").Count).IsEqualTo(0);
	}

	[Test]
	public async Task OnADesktop_ThereIsNoMenuButton()
	{
		var cut = RenderPlay(new ShellNavigation(() => { }));
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".play-me"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("button.play-menu-btn").Count).IsEqualTo(0).Because("a desktop has the rail");
	}

	[Test]
	public async Task TheViewersPicture_IsTheirRowInTheRoomsContents()
	{
		var cut = RenderPlay();
		PushRoom();
		_store.Set(OobEntryParser.RoomContentsPackage,
			"""{"v":2,"who":[{"dbref":"#313","objid":"#313:1","type":"player","name":"Ilsa Varn","cmd":"look #313","you":true,"image":{"url":"/c/ilsa.jpg"}}]}""");
		cut.WaitForAssertion(() => cut.Find("img.play-me-img"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find("img.play-me-img").GetAttribute("src")).EndsWith("/c/ilsa.jpg");
	}

	[Test]
	public async Task TheConnectionControls_AreInTheHeader_AndTheTerminalHasNoConnectionRow()
	{
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find("button.play-conn:not(.play-room-btn)"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play .sharp-terminal-connbar").Count).IsEqualTo(0);
		await Assert.That(cut.Find("button.play-conn:not(.play-room-btn)").GetAttribute("aria-label")).IsEqualTo("Disconnect");

		await cut.Find("button.play-conn:not(.play-room-btn)").ClickAsync();
		await _play.Received(1).DisconnectAsync();
	}

	[Test]
	public async Task FocusMode_StepsTheSidebarBannerAndAsideAway()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".play-aside"), TimeSpan.FromSeconds(5));
		await cut.Find("button.scene-card-focus").ClickAsync();
		await Assert.That(cut.FindAll(".test-pagebar .kit-pagebar").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".play-aside").HasAttribute("hidden")).IsTrue()
			.Because("the aside stays mounted, so its exit keys keep working in focus mode");
		await Assert.That(cut.FindComponents<ExitsCard>().Count).IsEqualTo(1);
		await Assert.That(cut.FindComponents<GlobalTerminal>().Count).IsEqualTo(1);
		await cut.Find("button.scene-card-focus").ClickAsync();
		await Assert.That(cut.Find(".play-aside").HasAttribute("hidden")).IsFalse();
	}

	[Test]
	public async Task TheComposer_SendsThroughThePlayConnection()
	{
		var cut = RenderPlay();
		PushRoom();
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".composer textarea"), TimeSpan.FromSeconds(5));
		await cut.Find(".composer textarea").InputAsync("leans on the crates");
		await cut.Find("button.composer-send").ClickAsync();
		await _play.Received(1).SendAsync("say leans on the crates");
	}

	[Test]
	public async Task ARefusedLiveSubscription_WarnsInTheStory_AndTheComposerDoesNotSend()
	{
		_hub.JoinRefusal = new Microsoft.AspNetCore.SignalR.HubException("no character can see this scene");
		var cut = RenderPlay();
		PushRoom();
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".play-story .play-story-unavailable"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play-story-unavailable").GetAttribute("role")).IsEqualTo("status");

		await cut.Find(".composer textarea").InputAsync("leans on the crates");
		await Assert.That(cut.Find("button.composer-send").HasAttribute("disabled")).IsTrue()
			.Because("this client would never see the pose arrive in the story");
		await cut.Find("button.composer-send").ClickAsync();
		await _play.DidNotReceive().SendAsync(Arg.Any<string>());
	}

	[Test]
	public async Task ACharacterInHere_OpensTheSheet_AndPageStartsAPageInTheComposer()
	{
		var cut = RenderPlay();
		PushRoom();
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".play-aside .kit-portrait"), TimeSpan.FromSeconds(5));
		await cut.Find(".play-aside .kit-portrait").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".sheet[role='dialog']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".sheet-name").TextContent).IsEqualTo("Tomas Reyes");
		await cut.FindAll(".sheet-actions > *")[1].ClickAsync();
		await Assert.That(cut.FindAll(".sheet").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".composer textarea").GetAttribute("value")).IsEqualTo("page Tomas Reyes=");
	}

	[Test]
	public async Task ThePeopleButton_OpensTheRoomPanelFromTheRight_WhoseExitsAreRowsThatGo()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find("button.play-room-btn"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-tabs, .play-tab").Count).IsEqualTo(0).Because("a chat app has no tab bar under its messages");
		await cut.Find("button.play-room-btn").ClickAsync();
		var sheet = cut.Find(".play-sheet[role='dialog']");
		await Assert.That(sheet.GetAttribute("aria-modal")).IsEqualTo("true");
		await Assert.That(sheet.ClassList).Contains("play-sheet--room");
		await Assert.That(cut.Find(".mud-overlay").GetAttribute("style")).Contains("justify-content: flex-end")
			.Because("MudOverlay centres a zero-size content box; the Room panel sits on the right edge instead");
		var sheetTabs = cut.FindAll(".play-sheet [role='tab']").Select(t => t.TextContent.Trim()).ToList();
		await Assert.That(sheetTabs).IsEquivalentTo(new[] { "Here · 1", "Exits · 1", "Weather" }, TUnit.Assertions.Enums.CollectionOrdering.Matching)
			.Because("the Room sheet is the play layout, then the scope panels, as the desktop aside is");
		await Assert.That(cut.FindAll(".play-sheet .exits--rows .exit").Count).IsEqualTo(1);
		await cut.Find(".play-sheet .exit button.exit-go").ClickAsync();
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
		await cut.Find("button.play-room-btn").ClickAsync();
		// The sheet's tabs can land on a later render than the click on a loaded runner; wait for them.
		string[] expected = ["Here · 1", "Exits · 1", "Weather"];
		cut.WaitForAssertion(() =>
		{
			var tabs = cut.FindAll(".play-sheet [role='tab']").Select(t => t.TextContent.Trim()).ToArray();
			if (!tabs.SequenceEqual(expected)) throw new InvalidOperationException($"tabs: [{string.Join(", ", tabs)}]");
		}, TimeSpan.FromSeconds(5));
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
		await cut.Find("button.play-room-btn").ClickAsync();
		// The sheet's tabs can land on a later render than the click on a loaded runner; wait for them.
		string[] expected = ["Here · 1", "Exits · 1"];
		cut.WaitForAssertion(() =>
		{
			var tabs = cut.FindAll(".play-sheet [role='tab']").Select(t => t.TextContent.Trim()).ToArray();
			if (!tabs.SequenceEqual(expected)) throw new InvalidOperationException($"tabs: [{string.Join(", ", tabs)}]");
		}, TimeSpan.FromSeconds(5));
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
		await Assert.That(cut.Find(".test-pagebar .play-side-scene .kit-row").GetAttribute("aria-current")).IsNull()
			.Because("walking in shows the terminal");
		await OpenStory(cut);
		await Assert.That(cut.Find(".test-pagebar .play-side-scene .kit-row").GetAttribute("aria-current")).IsEqualTo("page");
		await cut.FindAll(".scene-card-radio")[1].ClickAsync();
		await Assert.That(cut.Find(".test-pagebar .play-side-scene .kit-row").GetAttribute("aria-current")).IsNull();
	}

	[Test]
	public async Task PageFromTheSheet_UsesTheRowsPageAction_WhichNamesTheDbref()
	{
		var cut = RenderPlay();
		_store.Set(OobEntryParser.RoomInfoPackage, """{"v":2,"name":"Lower Docks","scene":{"id":"42","title":"Salt Market"}}""");
		_store.Set(OobEntryParser.RoomContentsPackage,
			"""{"v":2,"who":[{"dbref":"#312","type":"player","name":"Tomas Reyes","cmd":"look #312","profile":true,"actions":[{"label":"Page","cmd":"page #312="}]}]}""");
		await OpenStory(cut);
		cut.WaitForAssertion(() => cut.Find(".play-aside .kit-portrait"), TimeSpan.FromSeconds(5));
		await cut.Find(".play-aside .kit-portrait").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".sheet"), TimeSpan.FromSeconds(5));
		await cut.FindAll(".sheet-actions > *")[1].ClickAsync();
		await Assert.That(cut.Find(".composer textarea").GetAttribute("value")).IsEqualTo("page #312=");
	}

	[Test]
	public async Task OnTouchChrome_AnOpenChannel_LeadsItsHeaderWithTheMenuButton()
	{
		// The channel view hides the card, and with it the card's menu button; the shell's header stays
		// merged away, so the channel header carries the menu button instead.
		JSInterop.Setup<bool>("sharpmushLayout.isTouchChrome").SetResult(true);
		_comms.ChannelList = [new CommChannel("Public", 1)];
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find("button.play-menu-btn"), TimeSpan.FromSeconds(5));
		await cut.Find(".test-pagebar .play-side-channels .kit-row").ClickAsync();

		await Assert.That(cut.Find(".play-card").HasAttribute("hidden")).IsTrue();
		var head = cut.Find(".comm-head");
		await Assert.That(head.FirstElementChild!.ClassList).Contains("play-menu-btn");
		await head.QuerySelector("button.play-menu-btn")!.ClickAsync();
		await Assert.That(cut.Find(".play-sheet[role='dialog']").ClassList).Contains("play-sheet--side");
	}

	[Test]
	public async Task OnADesktop_TheChannelHeader_HasNoMenuButton()
	{
		_comms.ChannelList = [new CommChannel("Public", 1)];
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".test-pagebar .play-side-channels .kit-row"), TimeSpan.FromSeconds(5));
		await cut.Find(".test-pagebar .play-side-channels .kit-row").ClickAsync();
		await Assert.That(cut.FindAll(".comm-head .play-menu-btn").Count).IsEqualTo(0).Because("a desktop has the rail and the sidebar");
	}

	[Test]
	public async Task AChannelRow_OpensTheChannelViewInMain_AndCloseReturnsToTheScene()
	{
		_comms.ChannelList = [new CommChannel("Public", 1)];
		_comms.Lines["Public"] = [new CommMessage("channel", "Public", [], "Wren Halloway", null, "anyone up for a scene?", DateTimeOffset.Now)];
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".test-pagebar .play-side-channels .kit-row"), TimeSpan.FromSeconds(5));
		await cut.Find(".test-pagebar .play-side-channels .kit-row").ClickAsync();

		await Assert.That(cut.Find(".comm-title").TextContent).IsEqualTo("# Public");
		await Assert.That(cut.Find(".play-card").HasAttribute("hidden")).IsTrue();
		await Assert.That(cut.FindComponents<GlobalTerminal>().Count).IsEqualTo(1).Because("the terminal keeps its connection and output");
		await Assert.That(cut.Find(".test-pagebar .play-side-channels .kit-row").GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.Find(".test-pagebar .play-side-scene .kit-row").GetAttribute("aria-current")).IsNull();

		await cut.Find(".comm-compose input").InputAsync("I'm in");
		await cut.Find(".comm-compose button.comm-send").ClickAsync();
		await _play.Received(1).SendAsync("@chat Public=I'm in");

		await cut.Find("button.comm-close").ClickAsync();
		// Closing re-renders once the comms service drops the view; wait for that rather than racing it.
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".comm").Count != 0) throw new InvalidOperationException("channel view still open");
		}, TimeSpan.FromSeconds(5));
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
	public async Task OnALargerScreen_OpeningAndFoldingTheBanner_IsRemembered()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find("button.scene-card-sub--action"), TimeSpan.FromSeconds(5));
		await cut.Find("button.scene-card-sub--action").ClickAsync();
		cut.WaitForAssertion(() => cut.Find(".kit-banner"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-banner").Count).IsEqualTo(1);
		await cut.Find("button.kit-banner-minimise").ClickAsync();
		cut.WaitForState(() => cut.FindAll(".kit-banner, .kit-banner-strip").Count == 0, TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-banner, .kit-banner-strip").Count).IsEqualTo(0).Because("minimise folds it back into the header, not to a strip");
		// Read the invocations on the renderer's dispatcher: other components are still making JS calls there.
		var stored = await cut.InvokeAsync(() => JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem")
			.Select(i => i.Arguments).Where(a => (string?)a[0] == "play.banner").Select(a => (string?)a[1]).ToList());
		await Assert.That(stored).IsEquivalentTo(new[] { "open", "folded" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task OnALargerScreen_ARememberedOpenBanner_StartsOpen()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "play.banner").SetResult("open");
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".kit-banner"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".scene-card-head-img").Count).IsEqualTo(0).Because("the banner carries the picture");
		await Assert.That(cut.FindAll(".scene-card > .scene-card-banner .kit-banner").Count).IsEqualTo(1)
			.Because("the opened banner is the scene card's top, not a box of its own above it");
	}

	[Test]
	public async Task OnACompactScreen_TheBannerFoldsIntoTheCardHeader_AndOpensAsASheetOverThePage()
	{
		JSInterop.Setup<bool>("sharpmushLayout.watchCompactScreen", _ => true).SetResult(true);
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find("button.scene-card-sub--action"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".kit-banner, .kit-banner-strip").Count).IsEqualTo(0).Because("one header row, not a strip over a header");
		await Assert.That(cut.Find(".scene-card-sub-text").TextContent).IsEqualTo("Lower Docks").Because("the header names the room instead");
		await Assert.That(cut.Find("button.scene-card-sub--action").GetAttribute("aria-label")).IsEqualTo("Lower Docks: Show banner")
			.Because("the room's name is the button that opens the banner");

		await cut.Find(".scene-card-title").ClickAsync();
		var sheet = cut.Find(".play-sheet.play-sheet--details[role='dialog']");
		await Assert.That(sheet.GetAttribute("aria-label")).IsEqualTo("Lower Docks");
		await Assert.That(cut.FindAll(".play-sheet--details .kit-banner").Count).IsEqualTo(1).Because("the full view is one press away");
		await Assert.That(cut.FindAll(".play-main .kit-banner").Count).IsEqualTo(0).Because("it drops over the page, not above the card: one bar");
		await Assert.That(cut.FindAll(".play-sheet--details .play-banner-more .play-conn[aria-label='Disconnect']").Count).IsEqualTo(1);
		await cut.Find(".play-sheet--details button.kit-banner-minimise").ClickAsync();
		await Assert.That(cut.FindAll(".play-sheet, .kit-banner, .kit-banner-strip").Count).IsEqualTo(0).Because("minimising closes it");

		var stored = await cut.InvokeAsync(() =>
			JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem").Select(i => i.Arguments).ToList());
		await Assert.That(stored.Any(a => (string?)a[0] == "play.banner")).IsFalse()
			.Because("a choice made on a sideways phone must not change the banner on a taller screen");
	}

	[Test]
	public async Task TheBanner_CarriesThePhonesControlsRow_FocusSettingsAndDisconnect()
	{
		// Discord's channel details: on a phone, Play.razor.css hides the header's less frequent buttons
		// (.play-head-more, the focus button) once a room has come, and the name opens them in the details sheet.
		JSInterop.Setup<bool>("sharpmushLayout.watchCompactScreen", _ => true).SetResult(true);
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".play.play--room"), TimeSpan.FromSeconds(5));
		var more = cut.FindAll(".scene-card-head .play-head-more");
		await Assert.That(more.Count).IsEqualTo(2);
		await Assert.That(more[0].QuerySelector(".play-settings-btn")).IsNotNull();
		await Assert.That(more[1].QuerySelector(".play-conn[aria-label='Disconnect']")).IsNotNull();

		await cut.Find(".scene-card-title").ClickAsync();
		var row = cut.Find(".play-sheet--details .play-banner-more");
		await Assert.That(row.QuerySelector(".play-settings-btn")).IsNotNull();
		await Assert.That(row.QuerySelector(".play-conn[aria-label='Disconnect']")).IsNotNull();
		await row.QuerySelector("button.play-banner-focus")!.ClickAsync();
		await Assert.That(cut.Find(".play").ClassList).Contains("play--focus");
		await Assert.That(cut.FindAll(".play-sheet--details").Count).IsEqualTo(0).Because("entering focus closes the sheet it was opened from");

		await cut.Find("button.scene-card-focus").ClickAsync();
		await cut.Find(".scene-card-title").ClickAsync();
		await cut.Find(".play-sheet--details .play-conn[aria-label='Disconnect']").ClickAsync();
		await _play.Received(1).DisconnectAsync();
	}

	[Test]
	public async Task TurningTheScreen_ClosesAnOpenPhoneSheet()
	{
		JSInterop.Setup<bool>("sharpmushLayout.watchCompactScreen", _ => true).SetResult(true);
		JSInterop.Setup<bool>("sharpmushLayout.isTouchChrome").SetResult(true);
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find("button.play-room-btn"), TimeSpan.FromSeconds(5));
		await cut.Find("button.play-room-btn").ClickAsync();
		await Assert.That(cut.FindAll(".play-sheet--room").Count).IsEqualTo(1);

		var page = cut.FindComponent<PlayPage>();
		await cut.InvokeAsync(() => page.Instance.OnCompactScreenChanged(false));
		await Assert.That(cut.FindAll(".play-sheet").Count).IsEqualTo(0).Because("a larger screen has the aside beside the card");
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "sharpmushLayout.restoreFocus")).IsTrue()
			.Because("closing hands focus back to what opened it");
	}

	[Test]
	public async Task Disconnected_ConnectStaysInTheHeader()
	{
		_play.IsConnected.Returns(false);
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".scene-card-head .play-conn--connect"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-card-head .play-conn--connect").Closest(".play-head-more")).IsNull()
			.Because("the way in is never tucked away");
	}

	[Test]
	public async Task TheRoomsPicture_IsInTheCardHeader_WhileNoBannerShowsIt()
	{
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".scene-card-head-img"), TimeSpan.FromSeconds(5));
		await cut.Find("button.scene-card-sub--action").ClickAsync();
		await Assert.That(cut.FindAll(".scene-card-head-img").Count).IsEqualTo(0).Because("the opened banner shows it");

		await cut.Find("button.scene-card-focus").ClickAsync();
		await Assert.That(cut.Find(".scene-card-head-img").GetAttribute("src")).EndsWith("/r/docks.jpg").Because("focus hides the banner");
	}

	[Test]
	public async Task OnACompactScreen_TheFoldedHeaderCarriesTheRoomsPicture()
	{
		JSInterop.Setup<bool>("sharpmushLayout.watchCompactScreen", _ => true).SetResult(true);
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find(".scene-card-head-img"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".scene-card-head-img").GetAttribute("src")).EndsWith("/r/docks.jpg");

		await cut.Find("button.scene-card-sub--action").ClickAsync();
		await Assert.That(cut.Find(".scene-card-head-img").GetAttribute("src")).EndsWith("/r/docks.jpg").Because("the sheet drops over the header, which keeps it");
	}

	[Test]
	public async Task OnAShortScreen_ThePageIsMarkedShort_AndFollowsTheScreenAsItTurns()
	{
		// #1506: a phone held sideways is wider than the narrow tier and short of height for the tablet layout;
		// Play.razor.css tightens that layout under play--short.
		JSInterop.Setup<bool>("sharpmushLayout.watchShortScreen", _ => true).SetResult(true);
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".play.play--short"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll(".play-aside").Count).IsEqualTo(1).Because("it keeps the tablet layout, aside and all");

		var page = cut.FindComponent<PlayPage>();
		await cut.InvokeAsync(() => page.Instance.OnShortScreenChanged(false));
		await Assert.That(cut.Find(".play").ClassList).DoesNotContain("play--short").Because("turned upright");
		await cut.InvokeAsync(() => page.Instance.OnShortScreenChanged(true));
		await Assert.That(cut.Find(".play").ClassList).Contains("play--short");

		await cut.Find("button.scene-card-focus").ClickAsync();
		await Assert.That(cut.Find(".play").ClassList).Contains("play--focus");
		await Assert.That(cut.Find(".play").ClassList).Contains("play--short").Because("focus mode is tightened the same way");

		await DisposeComponentsAsync();
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "sharpmushLayout.unwatchShortScreen")).IsTrue()
			.Because("a page that is gone must not be called when the screen turns");
	}

	[Test]
	public async Task ATallerScreen_IsNotMarkedShort()
	{
		var cut = RenderPlay();
		PushRoom(scene: false);
		cut.WaitForAssertion(() => cut.Find(".play.play--room"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.Find(".play").ClassList).DoesNotContain("play--short");
		await Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "sharpmushLayout.watchShortScreen")).IsTrue()
			.Because("the page asks, and follows the answer as the screen turns");
	}

	[Test]
	public async Task TurningTheScreen_PutsTheBannerBackToItsDefaultForThatScreen()
	{
		JSInterop.Setup<bool>("sharpmushLayout.watchCompactScreen", _ => true).SetResult(true);
		var cut = RenderPlay();
		PushRoom();
		cut.WaitForAssertion(() => cut.Find("button.scene-card-sub--action"), TimeSpan.FromSeconds(5));
		await cut.Find("button.scene-card-sub--action").ClickAsync();
		var page = cut.FindComponent<PlayPage>();

		await Assert.That(cut.FindAll(".play-sheet--details").Count).IsEqualTo(1);
		await cut.InvokeAsync(() => page.Instance.OnCompactScreenChanged(false));
		await Assert.That(cut.FindAll(".play-sheet--details, .kit-banner").Count).IsEqualTo(0).Because("a larger screen follows its own remembered choice (folded), not the phone's sheet");

		await cut.InvokeAsync(() => page.Instance.OnCompactScreenChanged(true));
		await Assert.That(cut.FindAll(".kit-banner, .kit-banner-strip").Count).IsEqualTo(0).Because("each turn back to the compact screen folds it again");
		await Assert.That(cut.FindAll("button.scene-card-sub--action").Count).IsEqualTo(1);
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
