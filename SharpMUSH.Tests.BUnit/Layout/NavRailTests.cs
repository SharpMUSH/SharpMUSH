using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Navigation;
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The D1 rail (README §3): one 44×44 icon per section with an accessible name, the current section
/// marked, Mail only while a character is active, Build &amp; manage only when something in it is
/// visible, and the search, terminal, language and account controls at the bottom.
/// </summary>
public class NavRailTests : TrackingBunitContext
{
	/// <summary>
	/// Answers <c>api/applications</c> with <see cref="Apps"/> (none unless a test adds some) and <c>api/navigation</c>
	/// with <see cref="Layout"/> (none stored unless a test sets one).
	/// </summary>
	private sealed class AppsHandler : HttpMessageHandler
	{
		public object[] Apps { get; set; } = [];

		public NavLayout? Layout { get; set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(request.RequestUri!.AbsolutePath.TrimStart('/') switch
			{
				"api/applications" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Apps) },
				"api/navigation" when Layout is not null => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Layout) },
				"api/navigation" => new HttpResponseMessage(HttpStatusCode.NoContent),
				_ => new HttpResponseMessage(HttpStatusCode.NotFound),
			});
	}

	private static object App(string slug, string placement) => new
	{
		slug,
		displayName = $"App {slug}",
		icon = (string?)null,
		kind = "Page",
		schemaUrl = $"http/{slug}/schema",
		minimumRole = "Guest",
		navPlacement = placement,
		zones = Array.Empty<string>(),
		order = 0,
	};

	/// <summary>Play inline, then the boards and highlights applications as a pop-out named Community, then World.</summary>
	private void StoreCommunityPopOut(string? icon = null)
	{
		_apps.Apps = [App("boards", "Community"), App("highlights", "Community")];
		_apps.Layout = new NavLayout(NavLayout.CurrentFormat, 1,
		[
			new("play", null, NavGroupModes.Inline, null, [new("core:play"), new("core:scenes")]),
			new("community", "Community", NavGroupModes.Flyout, icon, [new("app:boards"), new("app:highlights")]),
			new("world", null, NavGroupModes.Inline, null, [new("core:wiki"), new("core:characters"), new("core:help")]),
		], [], []);
	}

	/// <summary>The rail under a popover provider, so a pop-out's list can be drawn.</summary>
	private IRenderedComponent<MudHarness> RenderWithPopoversAt(string path)
	{
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo(path);
		return Render<MudHarness>(p => p.AddChildContent<NavRail>());
	}

	private readonly AppsHandler _apps = new();

	private readonly BunitAuthorizationContext _auth;
	private readonly ITerminalService _terminal;

	public NavRailTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		_terminal = Substitute.For<ITerminalService>();
		var terminalHost = new TerminalServiceHost(() => _terminal);
		Services.AddSingleton(terminalHost);
		Services.AddSingleton<ITerminalService>(terminalHost);
		var play = Substitute.For<IPlayTerminalService>();
		var playHost = new PlayTerminalServiceHost(() => play);
		Services.AddSingleton(playHost);
		Services.AddSingleton<IPlayTerminalService>(playHost);
		Services.AddSingleton(Substitute.For<IConnectionStateService>());
		Services.AddSingleton<TerminalResumeStore>();
		Services.AddSingleton<CharacterSwitchService>();

		var client = Track(new HttpClient(_apps) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddSingleton(factory);
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []));
		_auth = AddAuthorization();
	}

	private IRenderedComponent<NavRail> RenderAt(string path, bool terminalOpen = false, Action? onSearch = null)
	{
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo(path);
		return Render<NavRail>(p => p.Add(x => x.TerminalOpen, terminalOpen)
			.Add(x => x.OnSearch, () => onSearch?.Invoke()));
	}

	[Test]
	public async Task EverySection_IsAnIconWithAName()
	{
		var cut = RenderAt("/");
		foreach (var href in new[] { "/play", "/scenes", "/wiki", "/characters", "/help" })
		{
			var item = cut.Find($"a.phosphor-rail-item[href='{href}']");
			await Assert.That(item.GetAttribute("aria-label")).IsNotNull().Because($"{href} is an icon-only link");
		}
		await Assert.That(cut.Find("nav.phosphor-rail").GetAttribute("aria-label")).IsNotNull();
	}

	/// <summary>The rail links the scene archive only while the game has the Scene System.</summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task TheSceneArchive_IsLinked_OnlyWhenTheGameHasScenes(bool scenes)
	{
		Services.AddSingleton<ServerInfoService>(
			new StubServerInfoService(guestsEnabled: true, features: scenes ? [GameFeatures.Scenes] : []));

		var cut = RenderAt("/");

		await Assert.That(cut.FindAll("a.phosphor-rail-item[href='/scenes']").Count).IsEqualTo(scenes ? 1 : 0);
		await Assert.That(cut.FindAll("a.phosphor-rail-item[href='/wiki']").Count).IsEqualTo(1);
	}

	/// <summary>The logo is the only link home; a separate Home icon went to the same place.</summary>
	[Test]
	public async Task TheLogo_IsTheOnlyHomeLink_AndIsMarkedOnTheHomePage()
	{
		var cut = RenderAt("/");
		var links = cut.FindAll("nav.phosphor-rail a[href='/']");
		await Assert.That(links.Count).IsEqualTo(1);
		var logo = cut.Find("a.phosphor-rail-logo");
		await Assert.That(logo.GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(logo.GetAttribute("aria-label")).Contains("Home");
	}

	/// <summary>The logo is the game's portal_logo when it has one, else the SharpMUSH mark in the theme's accent.</summary>
	[Test]
	[Arguments(null)]
	[Arguments("/api/wiki-assets/abc/crest.png")]
	public async Task TheLogo_IsTheGamesPicture_ElseTheSharpMUSHMark(string? configured)
	{
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true, logo: configured));

		var logo = RenderAt("/").Find("a.phosphor-rail-logo");

		if (configured is null)
		{
			await Assert.That(logo.QuerySelector("svg g")!.GetAttribute("fill")).IsEqualTo("var(--accent)");
			await Assert.That(logo.QuerySelector("img")).IsNull();
		}
		else
		{
			await Assert.That(logo.QuerySelector("img")!.GetAttribute("src")).IsEqualTo(configured);
			await Assert.That(logo.QuerySelector("svg")).IsNull();
		}
	}

	[Test]
	public async Task TheRail_LinksEachDestinationOnce()
	{
		_terminal.IsConnected.Returns(true);
		var cut = RenderAt("/");
		var hrefs = cut.FindAll("nav.phosphor-rail a[href]").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(hrefs.Distinct().Count()).IsEqualTo(hrefs.Count);
	}

	[Test]
	public async Task TheCurrentSection_IsMarked_IncludingDeeperPages()
	{
		var cut = RenderAt("/wiki/main/theme/harbour_ward");
		var wiki = cut.Find("a.phosphor-rail-item[href='/wiki']");
		await Assert.That(wiki.GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(wiki.ClassList).Contains("phosphor-rail-item--on");
		await Assert.That(cut.Find("a.phosphor-rail-logo").GetAttribute("aria-current")).IsNull();

		var profile = RenderAt("/character/Tomas%20Reyes");
		await Assert.That(profile.Find("a.phosphor-rail-item[href='/characters']").GetAttribute("aria-current")).IsEqualTo("page");
	}

	[Test]
	public async Task Mail_OnlyWhileACharacterIsActive()
	{
		_terminal.IsConnected.Returns(false);
		await Assert.That(RenderAt("/").FindAll("a[href='/mail']").Count).IsEqualTo(0);

		_terminal.IsConnected.Returns(true);
		var cut = RenderAt("/");
		await Assert.That(cut.FindAll("a[href='/mail']").Count).IsEqualTo(1);
		var order = cut.FindAll("a.phosphor-rail-item").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(order.IndexOf("/mail")).IsLessThan(order.IndexOf("/help")).Because("README §3: Play, Scenes, Wiki, Characters, Mail; Help follows");
	}

	[Test]
	public async Task BuildAndManage_OnlyWhenSomethingInItIsVisible_AndMarkedOnItsPages()
	{
		_auth.SetAuthorized("player");
		var player = RenderAt("/");
		await Assert.That(player.FindAll(".phosphor-rail-build").Count).IsEqualTo(0);

		_auth.SetPolicies("queue.inspect.own");
		await Assert.That(RenderAt("/").FindAll(".phosphor-rail-build").Count).IsEqualTo(0)
			.Because("a player's own queue is not a staff page");

		_auth.SetPolicies("queue.inspect");
		var staff = RenderAt("/admin/diagnostics");
		var build = staff.Find("a.phosphor-rail-build");
		await Assert.That(build.GetAttribute("href")).IsEqualTo("/admin/diagnostics").Because("its first visible destination");
		await Assert.That(build.GetAttribute("aria-current")).IsEqualTo("page");
	}

	[Test]
	public async Task RoleGatedApps_FollowTheSignedInRole_WithoutARemount()
	{
		_apps.Apps =
		[
			new
			{
				slug = "council", displayName = "Council", icon = (string?)null, kind = "Page", schemaUrl = "/apps/council/schema",
				dataUrl = (string?)null, submitRoute = (string?)null, minimumRole = "Wizard", navPlacement = "Council",
				zones = Array.Empty<string>(), order = 0,
			},
		];
		_auth.SetAuthorized("wiz");
		_auth.SetRoles("Wizard");
		var cut = RenderAt("/");
		cut.WaitForAssertion(() => cut.Find("a.phosphor-rail-item[href='/apps/council']"), TimeSpan.FromSeconds(5));

		_auth.SetNotAuthorized();
		cut.WaitForState(() => cut.FindAll("a[href='/apps/council']").Count == 0, TimeSpan.FromSeconds(2));
		await Assert.That(cut.FindAll("a[href='/apps/council']").Count).IsEqualTo(0)
			.Because("signing out leaves the Wizard-only section behind the previous role");
	}

	/// <summary>
	/// The rail draws the game's navigation groups: each inline group a list named for it, and an application
	/// placed in Play joins Play's list rather than going without a button.
	/// </summary>
	[Test]
	public async Task InlineGroups_AreNamedLists_AndAPlayAppJoinsPlay()
	{
		_apps.Apps =
		[
			new
			{
				slug = "chargen", displayName = "Chargen", icon = (string?)null, kind = "Page", schemaUrl = "http/chargen/schema",
				minimumRole = "Guest", navPlacement = "Play", zones = Array.Empty<string>(), order = 0,
			},
		];
		var cut = RenderAt("/");

		cut.WaitForAssertion(() => cut.Find("ul[aria-label='Play'] a[href='/apps/chargen']"), TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("ul[aria-label='Play'] a[href='/play']").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll("ul[aria-label='NavSectionWorld'] a[href='/wiki']").Count).IsEqualTo(1);
	}

	/// <summary>Applications sharing a placement of their own are one button, opening the first of them.</summary>
	[Test]
	public async Task ASectionOfItsOwn_IsOneButton_ForItsFirstApp()
	{
		_apps.Apps =
		[
			new
			{
				slug = "boards", displayName = "Boards", icon = (string?)null, kind = "Page", schemaUrl = "http/boards/schema",
				minimumRole = "Guest", navPlacement = "Community", zones = Array.Empty<string>(), order = 2,
			},
			new
			{
				slug = "highlights", displayName = "Highlights", icon = (string?)null, kind = "Page", schemaUrl = "http/highlights/schema",
				minimumRole = "Guest", navPlacement = "Community", zones = Array.Empty<string>(), order = 1,
			},
		];
		var cut = RenderAt("/apps/boards");

		cut.WaitForAssertion(() => cut.Find("a.phosphor-rail-item[aria-label='Community']"), TimeSpan.FromSeconds(5));
		var community = cut.Find("a.phosphor-rail-item[aria-label='Community']");
		await Assert.That(community.GetAttribute("href")).IsEqualTo("/apps/highlights");
		await Assert.That(community.GetAttribute("aria-current")).IsEqualTo("page").Because("boards is in the section too");
		await Assert.That(cut.FindAll("a[href='/apps/boards']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task Search_OpensThePalette_AndTheTerminalToggleSaysWhetherItIsOpen()
	{
		var searched = false;
		var cut = RenderAt("/", terminalOpen: true, onSearch: () => searched = true);
		await cut.Find("button.phosphor-rail-search").ClickAsync();
		await Assert.That(searched).IsTrue();
		await Assert.That(cut.Find("button.phosphor-rail-terminal").GetAttribute("aria-pressed")).IsEqualTo("true");
	}

	[Test]
	public async Task TheAccount_IsTheCompactAvatar()
	{
		_auth.SetAuthorized("player");
		var cut = RenderAt("/");
		var card = cut.Find(".phosphor-rail .phosphor-profile-wrap--compact button.phosphor-profile-card");
		await Assert.That(card.GetAttribute("aria-label")).IsNotNull();
		await Assert.That(cut.FindAll(".phosphor-rail .phosphor-profile-name").Count).IsEqualTo(0);
	}

	/// <summary>
	/// A pop-out group is one button, named for the group and saying when it holds the page shown. Its links are drawn
	/// only once it is opened, and Escape closes it.
	/// </summary>
	[Test]
	public async Task APopOut_IsOneButton_WhoseLinksOpenBesideTheRail()
	{
		StoreCommunityPopOut();
		var cut = RenderWithPopoversAt("/apps/boards");

		var button = cut.WaitForElement("button.phosphor-rail-flyout-btn", TimeSpan.FromSeconds(5));
		await Assert.That(button.GetAttribute("aria-label")).IsEqualTo("NavFlyoutHoldsCurrent(Community)");
		await Assert.That(button.GetAttribute("aria-expanded")).IsEqualTo("false");
		await Assert.That(button.ClassList).Contains("phosphor-rail-item--on");
		await Assert.That(cut.FindAll("a[href='/apps/boards']")).IsEmpty();
		await Assert.That(cut.FindAll(".phosphor-rail-flyout-grid svg").Count).IsEqualTo(2).Because("it has no icon of its own");

		await button.ClickAsync();

		var boards = cut.WaitForElement("ul[aria-label='Community'] a[href='/apps/boards']");
		await Assert.That(boards.GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(cut.FindAll("ul[aria-label='Community'] a[href='/apps/highlights']").Count).IsEqualTo(1);
		button = cut.Find("button.phosphor-rail-flyout-btn");
		await Assert.That(button.GetAttribute("aria-expanded")).IsEqualTo("true");
		await Assert.That(button.GetAttribute("aria-controls")).IsEqualTo("rail-flyout-community");
		await Assert.That(cut.FindAll("#rail-flyout-community").Count).IsEqualTo(1);

		await button.KeyDownAsync(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

		await Assert.That(cut.Find("button.phosphor-rail-flyout-btn").GetAttribute("aria-expanded")).IsEqualTo("false");
	}

	/// <summary>Following a link, or any other change of page, closes the pop-out.</summary>
	[Test]
	public async Task APopOut_ClosesWhenThePageChanges()
	{
		StoreCommunityPopOut(icon: "forum");
		var cut = RenderWithPopoversAt("/");
		await cut.WaitForElement("button.phosphor-rail-flyout-btn", TimeSpan.FromSeconds(5)).ClickAsync();
		cut.WaitForElement("ul[aria-label='Community']");

		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/apps/highlights");

		cut.WaitForState(() => cut.Find("button.phosphor-rail-flyout-btn").GetAttribute("aria-expanded") == "false");
		await Assert.That(cut.Find("button.phosphor-rail-flyout-btn").GetAttribute("aria-label")).IsEqualTo("NavFlyoutHoldsCurrent(Community)");
		await Assert.That(cut.FindAll(".phosphor-rail-flyout-grid")).IsEmpty().Because("staff gave it an icon");
	}

	/// <summary>
	/// Every group stays on the rail, however many there are: the list scrolls instead, and the current page's
	/// button is scrolled into view when the page changes, not on every render.
	/// </summary>
	[Test]
	public async Task EveryGroupStaysOnTheRail_AndTheCurrentPageIsScrolledIntoView()
	{
		StoreCommunityPopOut();
		var cut = RenderWithPopoversAt("/wiki");
		cut.WaitForElement("button.phosphor-rail-flyout-btn", TimeSpan.FromSeconds(5));
		await Assert.That(cut.FindAll("li.phosphor-rail-group[data-group]").Select(li => li.GetAttribute("data-group")))
			.IsEquivalentTo(["play", "world", "community"]);

		int Reveals() => JSInterop.Invocations.Count(i => i.Identifier == "sharpmushLayout.revealRailCurrent");
		cut.WaitForState(() => Reveals() > 0);
		var before = Reveals();

		await cut.Find("button.phosphor-rail-flyout-btn").ClickAsync();
		await Assert.That(Reveals()).IsEqualTo(before).Because("opening a pop-out does not move the list");

		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/apps/highlights");
		cut.WaitForState(() => Reveals() > before);
	}
	/// <summary>
	/// An open pop-out closes when the rail's groups scroll its button out of sight; opening it is what starts the
	/// watch.
	/// </summary>
	[Test]
	public async Task ScrollingItsButtonOutOfSight_ClosesAnOpenPopOut()
	{
		StoreCommunityPopOut();
		var cut = RenderWithPopoversAt("/");
		await cut.WaitForElement("button.phosphor-rail-flyout-btn", TimeSpan.FromSeconds(5)).ClickAsync();
		cut.WaitForElement("ul[aria-label='Community']");
		cut.WaitForState(() => JSInterop.Invocations.Any(i => i.Identifier == "sharpmushLayout.closeOnRailScroll"));

		var flyout = cut.FindComponent<NavRailFlyout>();
		await flyout.InvokeAsync(() => flyout.Instance.OnRailScrolled());

		cut.WaitForState(() => cut.Find("button.phosphor-rail-flyout-btn").GetAttribute("aria-expanded") == "false");
	}
}
