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
using SharpMUSH.Client.Services;
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
	private readonly IPlayTerminalService _play = Substitute.For<IPlayTerminalService>();

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

		PlayPageServices.Install(Services);
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
		await Assert.That(cut.FindAll(".play-sheet .exits--rows .exit").Count).IsEqualTo(1);
		cut.Find(".play-sheet .exit button.exit-go").Click();
		await _play.Received(1).SendAsync("goto #1210");
		await Assert.That(cut.FindAll(".play-sheet").Count).IsEqualTo(0);
	}

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
				_ => null,
			};
			return Task.FromResult(body is null
				? new HttpResponseMessage(HttpStatusCode.NotFound)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
		}
	}
}
