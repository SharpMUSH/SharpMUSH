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
using SharpMUSH.Library.Models.Portal.Widgets;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>A game whose phone tabs are its Community group, then the wiki.</summary>
file sealed class GroupTabHandler : HttpMessageHandler
{
	private static object App(string slug) => new
	{
		slug,
		displayName = $"App {slug}",
		icon = (string?)null,
		kind = "Page",
		schemaUrl = $"http/{slug}/schema",
		minimumRole = "Guest",
		navPlacement = "Community",
		zones = Array.Empty<string>(),
		order = 0,
	};

	private static readonly NavLayout Layout = new(NavLayout.CurrentFormat, 1,
	[
		new("play", null, NavGroupModes.Inline, null, [new("core:play"), new("core:wiki")]),
		new("community", "Community", NavGroupModes.Flyout, null, [new("app:boards"), new("app:highlights")]),
	], [], ["group:community", "core:wiki"]);

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		=> Task.FromResult(request.RequestUri!.AbsolutePath.TrimStart('/') switch
		{
			"api/setup/status" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { needsSetup = false }) },
			"api/applications" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { App("boards"), App("highlights") }) },
			"api/navigation" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Layout) },
			_ => new HttpResponseMessage(HttpStatusCode.NotFound),
		});
}

/// <summary>
/// A phone tab staff gave a whole group opens the group's destinations in a sheet over the tab bar. The tab says
/// whether the sheet is open; the backdrop and a change of page close it.
/// </summary>
public class MainLayoutGroupTabTests : TrackingBunitContext
{
	public MainLayoutGroupTabTests()
	{
		var apiClient = Track(new HttpClient(new GroupTabHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient(Arg.Any<string>()).Returns(apiClient);

		var layouts = Substitute.For<ILayoutService>();
		layouts.GetLayoutAsync(Arg.Any<string>()).Returns(Task.FromResult(new LayoutConfiguration(
			new Dictionary<WidgetZone, List<WidgetPlacement>>(),
			new LayoutSettings(LeftSidebarEnabled: false, RightSidebarEnabled: false))));

		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		Services.AddSingleton(factory);
		Services.AddSingleton(layouts);
		Services.AddSingleton<IWidgetRegistry>(new WidgetRegistry());
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		Services.AddSingleton(new ApplicationCatalog([]));
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []));
		Services.AddSingleton(Substitute.For<IConnectionStateService>());
		Services.AddSingleton<TerminalResumeStore>();
		Services.AddSingleton<CharacterSwitchService>();
		Services.AddSingleton<TerminalLoginService>();
		Services.AddSingleton<PaletteClient>();
		Services.AddSingleton<ConfigurationSearchClient>();
		Services.AddSingleton<PaletteContext>();
		Services.AddSingleton<GameCommandService>();
		var terminal = new TerminalServiceHost(() => Substitute.For<ITerminalService>());
		Services.AddSingleton(terminal);
		Services.AddSingleton<ITerminalService>(terminal);
		var playTerminal = new PlayTerminalServiceHost(() => Substitute.For<IPlayTerminalService>());
		Services.AddSingleton(playTerminal);
		Services.AddSingleton<IPlayTerminalService>(playTerminal);
		var hostEnv = Substitute.For<Microsoft.AspNetCore.Components.WebAssembly.Hosting.IWebAssemblyHostEnvironment>();
		hostEnv.Environment.Returns("Production");
		Services.AddSingleton(hostEnv);
		JSInterop.Mode = JSRuntimeMode.Loose;
		AddAuthorization();
	}

	private IRenderedComponent<MainLayout> RenderAt(string path)
	{
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo(path);
		var cut = Render<MainLayout>();
		cut.WaitForElement("nav.phosphor-bottomnav button[aria-controls], nav.phosphor-bottomnav button.phosphor-bottomtab:not(:last-child)", TimeSpan.FromSeconds(5));
		return cut;
	}

	private static AngleSharp.Dom.IElement GroupTab(IRenderedComponent<MainLayout> cut) =>
		cut.FindAll("nav.phosphor-bottomnav button.phosphor-bottomtab").Single(b => b.TextContent.Contains("Community"));

	[Test]
	public async Task AGroupTab_OpensItsDestinationsInASheet()
	{
		var cut = RenderAt("/");

		var tab = GroupTab(cut);
		await Assert.That(tab.GetAttribute("aria-expanded")).IsEqualTo("false");
		await Assert.That(cut.FindAll("nav.phosphor-bottomnav a[href='/wiki']").Count).IsEqualTo(1).Because("a destination stays a link");
		await Assert.That(cut.FindAll("#nav-tabsheet")).IsEmpty();

		await tab.ClickAsync();

		tab = GroupTab(cut);
		await Assert.That(tab.GetAttribute("aria-expanded")).IsEqualTo("true");
		await Assert.That(tab.GetAttribute("aria-controls")).IsEqualTo("nav-tabsheet");
		await Assert.That(cut.Find(".phosphor-shell").ClassList).Contains("phosphor-shell--sheet-open");
		var sheet = cut.Find("#nav-tabsheet");
		await Assert.That(sheet.GetAttribute("role")).IsEqualTo("dialog");
		await Assert.That(cut.Find("#nav-tabsheet-title").TextContent).IsEqualTo("Community");
		await Assert.That(sheet.QuerySelectorAll("a").Select(a => a.GetAttribute("href"))).IsEquivalentTo(["/apps/boards", "/apps/highlights"]);

		await cut.Find(".phosphor-nav-backdrop").ClickAsync();

		await Assert.That(GroupTab(cut).GetAttribute("aria-expanded")).IsEqualTo("false");
		await Assert.That(cut.Find(".phosphor-shell").ClassList).DoesNotContain("phosphor-shell--sheet-open");
	}

	[Test]
	public async Task AGroupTab_IsLitOnItsPages_AndANewPageClosesTheSheet()
	{
		var cut = RenderAt("/apps/boards");
		await Assert.That(GroupTab(cut).ClassList).Contains("phosphor-bottomtab--on");

		await GroupTab(cut).ClickAsync();
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo("/apps/highlights");

		cut.WaitForState(() => GroupTab(cut).GetAttribute("aria-expanded") == "false");
		await Assert.That(cut.Find("#nav-tabsheet a[href='/apps/highlights']").GetAttribute("aria-current")).IsEqualTo("page");
	}
}
