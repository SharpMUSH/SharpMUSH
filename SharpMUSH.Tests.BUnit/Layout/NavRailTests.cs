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
using SharpMUSH.Library.Models.Portal.Setup;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The D1 rail (README §3): one 44×44 icon per section with an accessible name, the current section
/// marked, Mail only while a character is active, Build &amp; manage only when something in it is
/// visible, and the search, terminal, language and account controls at the bottom.
/// </summary>
public class NavRailTests : TrackingBunitContext
{
	/// <summary>Answers <c>api/applications</c> with <see cref="Apps"/> (none unless a test adds some).</summary>
	private sealed class AppsHandler : HttpMessageHandler
	{
		public object[] Apps { get; set; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(request.RequestUri!.AbsolutePath.TrimStart('/') == "api/applications"
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Apps) }
				: new HttpResponseMessage(HttpStatusCode.NotFound));
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
}
