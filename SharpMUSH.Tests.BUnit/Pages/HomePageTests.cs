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
/// D1 §6.5 home (boards 30, 31): the plain header over the admin-composed home scope, whose
/// MainContent is the page's main column and whose new RightSidebar zone is its aside — the same
/// shape as the wiki home. The layout still comes from the layout service; nothing is hard-coded.
/// </summary>
public class HomePageTests : BunitContext
{
	private static JsonElement Text(string markdown) => JsonSerializer.SerializeToElement(new { markdown, showToGuests = true });

	private void Arrange(LayoutConfiguration layout)
	{
		var registry = new WidgetRegistry();
		registry.Register(BuiltInWidgets.Named("WelcomeText"));
		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(LayoutScopes.Home).Returns(Task.FromResult(layout));
		Services
			.AddMudServices()
			.AddSingleton<IWidgetRegistry>(registry)
			.AddSingleton(layouts)
			.AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task ThePlainHeader_OverTheMainZone_BesideTheAsideZone()
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

		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("HomeWhatsHappening");
		await Assert.That(cut.Find(".home-main").TextContent).Contains("MainHello");
		await Assert.That(cut.Find(".home-main").TextContent).DoesNotContain("AsideHello");
		await Assert.That(cut.Find(".home-aside").TextContent).Contains("AsideHello");
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

		await Assert.That(cut.Find(".home-aside").ChildElementCount).IsEqualTo(0)
			.Because("Home.razor.css gives the column back to main when the aside is :empty");
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
