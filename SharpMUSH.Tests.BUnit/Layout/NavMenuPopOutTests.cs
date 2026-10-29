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
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>Answers api/applications with the boards and highlights applications and api/navigation with a Community pop-out.</summary>
file sealed class PopOutLayoutHandler : HttpMessageHandler
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
		new("play", null, NavGroupModes.Inline, null, [new("core:play")]),
		new("community", "Community", NavGroupModes.Flyout, null, [new("app:boards"), new("app:highlights")]),
	], [], []);

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		=> Task.FromResult(request.RequestUri!.AbsolutePath.TrimStart('/') switch
		{
			"api/applications" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { App("boards"), App("highlights") }) },
			"api/navigation" => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Layout) },
			_ => new HttpResponseMessage(HttpStatusCode.NotFound),
		});
}

/// <summary>
/// A pop-out group in the drawer is a disclosure: its name is a button that shows or hides its links. It is open
/// while it holds the page shown, and otherwise as the reader last left it in this browser.
/// </summary>
public class NavMenuPopOutTests : TrackingBunitContext
{
	public NavMenuPopOutTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		var terminal = Substitute.For<ITerminalService>();
		var terminalHost = new TerminalServiceHost(() => terminal);
		Services.AddSingleton(terminalHost);
		Services.AddSingleton<ITerminalService>(terminalHost);
		var play = Substitute.For<IPlayTerminalService>();
		var playHost = new PlayTerminalServiceHost(() => play);
		Services.AddSingleton(playHost);
		Services.AddSingleton<IPlayTerminalService>(playHost);
		Services.AddSingleton(Substitute.For<IConnectionStateService>());
		Services.AddSingleton<TerminalResumeStore>();
		Services.AddSingleton<CharacterSwitchService>();

		var client = Track(new HttpClient(new PopOutLayoutHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);
		Services.AddSingleton(factory);
		Services.AddSingleton(new ApplicationRegistryClient(factory, NullLogger<ApplicationRegistryClient>.Instance));
		Services.AddSingleton(new ApplicationCatalog([]));
		Services.AddSingleton(new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance, []));
		AddAuthorization();
	}

	private IRenderedComponent<NavMenu> RenderAt(string path)
	{
		Services.GetRequiredService<BunitNavigationManager>().NavigateTo(path);
		var cut = Render<NavMenu>(p => p.Add(c => c.IsCollapsed, false));
		cut.WaitForElement("button.nav-group-toggle", TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task APopOut_IsClosedElsewhere_AndOpeningItIsRemembered()
	{
		var cut = RenderAt("/");

		var toggle = cut.Find("button.nav-group-toggle");
		await Assert.That(toggle.GetAttribute("aria-expanded")).IsEqualTo("false");
		await Assert.That(toggle.GetAttribute("aria-controls")).IsEqualTo("drawer-group-community");
		await Assert.That(cut.Find("#drawer-group-community").HasAttribute("hidden")).IsTrue();

		await toggle.ClickAsync();

		await Assert.That(cut.Find("button.nav-group-toggle").GetAttribute("aria-expanded")).IsEqualTo("true");
		await Assert.That(cut.Find("#drawer-group-community").HasAttribute("hidden")).IsFalse();
		var stored = JSInterop.Invocations["localStorage.setItem"].Single();
		await Assert.That(stored.Arguments[0]).IsEqualTo("sharpmush.navgroup.community");
		await Assert.That(stored.Arguments[1]).IsEqualTo("1");
	}

	[Test]
	public async Task APopOut_IsOpen_WhileItHoldsThePageShown()
	{
		var cut = RenderAt("/apps/highlights");

		await Assert.That(cut.Find("button.nav-group-toggle").GetAttribute("aria-expanded")).IsEqualTo("true");
		await Assert.That(cut.Find("#drawer-group-community a[href='/apps/highlights']").ClassList).Contains("active");
	}

	[Test]
	public async Task APopOut_TheReaderLeftOpen_IsOpenOnTheirNextVisit()
	{
		JSInterop.Setup<string?>("localStorage.getItem", "sharpmush.navgroup.community").SetResult("1");

		var cut = RenderAt("/");

		await Assert.That(cut.Find("button.nav-group-toggle").GetAttribute("aria-expanded")).IsEqualTo("true");
	}
}
