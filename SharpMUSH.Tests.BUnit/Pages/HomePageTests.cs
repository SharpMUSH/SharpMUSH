using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Client.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// D1 §6.5 home (boards 30, 31): the game's hero over the admin-composed home scope, whose
/// MainContent is the page's main column and whose new RightSidebar zone is its aside — the same
/// shape as the wiki home. The layout still comes from the layout service; nothing is hard-coded.
/// </summary>
public class HomePageTests : BunitContext
{
	private static JsonElement Text(string markdown) => JsonSerializer.SerializeToElement(new { markdown, showToGuests = true });

	private readonly StubServerInfoService _serverInfo = new(guestsEnabled: true, gameName: "Gaslight");

	private void Arrange(LayoutConfiguration layout)
	{
		var registry = new WidgetRegistry();
		registry.Register(BuiltInWidgets.Named("WelcomeText"));
		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(LayoutScopes.Home).Returns(Task.FromResult(layout));
		Services
			.AddMudServices()
			.AddSingleton<IWidgetRegistry>(registry)
			.AddSingleton(new ApplicationCatalog([]))
			.AddSingleton(layouts)
			.AddSingleton<ServerInfoService>(_serverInfo)
			.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task TheGamesHero_OverTheMainZone_BesideTheAsideZone()
	{
		Arrange(new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("WelcomeText", 0, Text("MainHello"))],
				[WidgetZone.RightSidebar] = [new WidgetPlacement("WelcomeText", 0, Text("AsideHello"))],
			},
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false)));

		var cut = Render<SharpMUSH.Client.Pages.Home>();
		cut.WaitForAssertion(() => cut.Find(".home-aside .welcome-text-widget"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.Find(".home-main h1.game-hero-name").TextContent).IsEqualTo("Gaslight");
		await Assert.That(cut.Find(".home-main .home-section-title").TextContent).IsEqualTo("HomeWhatsHappening");
		await Assert.That(cut.Find(".home-main").TextContent).Contains("MainHello");
		await Assert.That(cut.Find(".home-main").TextContent).DoesNotContain("AsideHello");
		await Assert.That(cut.Find(".home-aside").TextContent).Contains("AsideHello");
		await Assert.That(cut.Find(".home-page").ClassList).Contains("home-page--aside");
	}

	[Test]
	public async Task ALayoutSavedBeforeTheAsideExisted_LeavesItEmpty()
	{
		Arrange(new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>
			{
				[WidgetZone.MainContent] = [new WidgetPlacement("WelcomeText", 0, Text("MainHello"))],
			},
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false)));

		var cut = Render<SharpMUSH.Client.Pages.Home>();
		cut.WaitForAssertion(() => cut.Find(".home-main .welcome-text-widget"), TimeSpan.FromSeconds(5));

		await Assert.That(cut.FindAll(".home-aside").Count).IsEqualTo(0)
			.Because("with nothing placed in the aside zone there is no aside, and main takes the width");
		await Assert.That(cut.Find(".home-page").ClassList).DoesNotContain("home-page--aside");
	}

	/// <summary>A configured game leads Home with its banner, tagline and description.</summary>
	[Test]
	public async Task AConfiguredGame_LeadsWithItsBannerTaglineAndDescription()
	{
		_serverInfo.Identity = ("/api/wiki-assets/a/banner.png", "Intrigue in a city that never sleeps", "Players run the guilds.");
		Arrange(new LayoutConfiguration(new Dictionary<WidgetZone, List<WidgetPlacement>>(),
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false)));

		var cut = Render<SharpMUSH.Client.Pages.Home>();

		var hero = cut.Find(".game-hero");
		await Assert.That(hero.ClassList).Contains("game-hero--image");
		await Assert.That(cut.Find(".game-hero-img").GetAttribute("src")).EndsWith("/api/wiki-assets/a/banner.png");
		await Assert.That(cut.Find(".game-hero-tagline").TextContent).IsEqualTo("Intrigue in a city that never sleeps");
		await Assert.That(cut.Find(".game-hero-description").TextContent).IsEqualTo("Players run the guilds.");
	}

	/// <summary>A fresh game has its name and logo on the theme's surface, and no empty tagline or description lines.</summary>
	[Test]
	public async Task AFreshGame_ShowsItsNameWithoutAPictureOrEmptyLines()
	{
		Arrange(new LayoutConfiguration(new Dictionary<WidgetZone, List<WidgetPlacement>>(),
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false)));

		var cut = Render<SharpMUSH.Client.Pages.Home>();

		await Assert.That(cut.Find(".game-hero").ClassList).DoesNotContain("game-hero--image");
		await Assert.That(cut.FindAll(".game-hero-img").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".game-hero-tagline, .game-hero-description").Count).IsEqualTo(0);
	}

	[Test]
	public async Task TheHomeScope_OffersTheAsideZone_AndItsDefaultIsOnlineThenGettingStarted()
	{
		var scope = LayoutScopes.Find(LayoutScopes.Home)!;
		await Assert.That(scope.Zones).Contains(WidgetZone.RightSidebar);

		var layout = new LayoutService(Substitute.For<IHttpClientFactory>(), Substitute.For<Microsoft.Extensions.Logging.ILogger<LayoutService>>())
			.GetDefaultLayout(LayoutScopes.Home);
		await Assert.That(layout.Zones[WidgetZone.RightSidebar].Select(p => p.WidgetName).ToList())
			.IsEquivalentTo(["OnlineCharacters", "Quickstart"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(layout.Zones[WidgetZone.MainContent].Select(p => p.WidgetName).ToList())
			.DoesNotContain("OnlineCharacters").And.DoesNotContain("Quickstart");
	}
}
