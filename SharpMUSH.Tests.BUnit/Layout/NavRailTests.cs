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
	private sealed class NoAppsHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			=> Task.FromResult(request.RequestUri!.AbsolutePath.TrimStart('/') == "api/applications"
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) }
				: new HttpResponseMessage(HttpStatusCode.NotFound));
	}

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
		Services.AddSingleton<CharacterSwitchService>();

		var client = Track(new HttpClient(new NoAppsHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
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
		foreach (var href in new[] { "/", "/play", "/scenes", "/wiki", "/characters", "/help" })
		{
			var item = cut.Find($"a.phosphor-rail-item[href='{href}']");
			await Assert.That(item.GetAttribute("aria-label")).IsNotNull().Because($"{href} is an icon-only link");
		}
		await Assert.That(cut.Find("nav.phosphor-rail").GetAttribute("aria-label")).IsNotNull();
	}

	[Test]
	public async Task TheCurrentSection_IsMarked_IncludingDeeperPages()
	{
		var cut = RenderAt("/wiki/main/theme/harbour_ward");
		var wiki = cut.Find("a.phosphor-rail-item[href='/wiki']");
		await Assert.That(wiki.GetAttribute("aria-current")).IsEqualTo("page");
		await Assert.That(wiki.ClassList).Contains("phosphor-rail-item--on");
		await Assert.That(cut.Find("a.phosphor-rail-item[href='/']").GetAttribute("aria-current")).IsNull();

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
		await Assert.That(order.IndexOf("/mail")).IsLessThan(order.IndexOf("/help")).Because("README §3: Home, Play, Scenes, Wiki, Characters, Mail; Help follows");
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
	public async Task Search_OpensThePalette_AndTheTerminalToggleSaysWhetherItIsOpen()
	{
		var searched = false;
		var cut = RenderAt("/", terminalOpen: true, onSearch: () => searched = true);
		cut.Find("button.phosphor-rail-search").Click();
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
