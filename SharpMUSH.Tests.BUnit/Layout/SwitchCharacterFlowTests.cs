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
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// Fakes the endpoints MainLayout's own init and the switch flow touch: <c>api/auth/account-login</c>
/// (seeds a real <see cref="AccountAuthService"/> with a two-character roster — same pattern as
/// <c>NavMenuActiveCharacterTests</c>), <c>api/applications</c> (NavMenu, rendered as MainLayout's
/// child), <c>api/setup/status</c> (MainLayout's own routing guard), and
/// <c>api/auth/switch-character</c> (the flow under test).
/// </summary>
file sealed class SwitchFlowApiHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Post && path == "api/auth/account-login")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "acct-1",
					username = "headwiz",
					characters,
					accountSessionToken = "session-token-1",
					mustChangePassword = false,
					role = "Wizard",
					permissions = new[] { "*" },
				})
			});
		}

		if (request.Method == HttpMethod.Get && path == "api/applications")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(Array.Empty<object>())
			});
		}

		if (request.Method == HttpMethod.Get && path == "api/setup/status")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new { needsSetup = false })
			});
		}

		if (request.Method == HttpMethod.Post && path == "api/auth/switch-character")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new { ott = "new-character-ott", expiresIn = 300 })
			});
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

file sealed class SwitchFlowStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Task 7: the character switch used to disconnect-then-reconnect the command terminal (the
/// reconnect resent the surviving resume token, risking a server-side rebind to the PREVIOUS
/// character's session and silently dropping the fresh OTT) and never touched the play terminal at
/// all, so it stayed logged in as the old character indefinitely. This exercises the real
/// <see cref="MainLayout"/> render tree — DI wiring mirrors <c>NavMenuActiveCharacterTests</c>
/// (already proven to render MainLayout's NavMenu child) plus <c>PluginChangeReloaderTests</c>'
/// <see cref="IConnectionStateService"/> fake and an unconfigured <see cref="ILayoutService"/>
/// substitute (so <c>_layout</c> stays null and MainLayout skips the widget-zone machinery
/// entirely) — so no MainLayout-impractical-to-render fallback was needed.
/// </summary>
public class SwitchCharacterFlowTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];
	private BunitAuthorizationContext Auth { get; }

	private static readonly CharacterSummary Alpha = new(1, 1L, "Alpha", "");
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	public SwitchCharacterFlowTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, SwitchFlowStubLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		// MainLayout's own OnInitializedAsync calls AccountAuth.InitAsync(), which is the FIRST call
		// to it in this test (CreateLoggedInAuthAsync's LoginAsync doesn't call InitAsync itself) —
		// so it genuinely re-hydrates from "sessionStorage". Without these, the unconfigured Loose
		// getItem calls return null and InitCoreAsync would wipe the session LoginAsync just set up.
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);

		Services.AddSingleton(Substitute.For<IConnectionStateService>());
		Services.AddSingleton(Substitute.For<ILayoutService>());

		Auth = this.AddAuthorization();
		Auth.SetAuthorized("headwiz");
	}

	private sealed record TerminalRig(TerminalServiceHost Host, ITerminalService First, ITerminalService Second);
	private sealed record PlayTerminalRig(PlayTerminalServiceHost Host, IPlayTerminalService First, IPlayTerminalService Second);

	/// <summary>
	/// Registers a real <see cref="TerminalServiceHost"/> (concrete type AND its interface aliased
	/// to the same instance, mirroring <c>TerminalServiceCollectionExtensions.AddTerminalServices</c>)
	/// backed by a two-substitute factory queue, so <c>RecreateAsync()</c> is observable via
	/// <c>DisposeAsync</c> on the first substitute.
	/// </summary>
	private TerminalRig RegisterTerminal()
	{
		var first = Substitute.For<ITerminalService>();
		var second = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, second]);
		var host = new TerminalServiceHost(() => queue.Dequeue());
		Services.AddSingleton(host);
		Services.AddSingleton<ITerminalService>(host);
		return new TerminalRig(host, first, second);
	}

	private PlayTerminalRig RegisterPlayTerminal()
	{
		var first = Substitute.For<IPlayTerminalService>();
		var second = Substitute.For<IPlayTerminalService>();
		var queue = new Queue<IPlayTerminalService>([first, second]);
		var host = new PlayTerminalServiceHost(() => queue.Dequeue());
		Services.AddSingleton(host);
		Services.AddSingleton<IPlayTerminalService>(host);
		return new PlayTerminalRig(host, first, second);
	}

	private async Task<AccountAuthService> CreateLoggedInAuthAsync()
	{
		var apiClient = new HttpClient(new SwitchFlowApiHandler([Alpha, Beta])) { BaseAddress = new Uri("https://localhost:8081/") };
		_ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services.AddSingleton(factory);
		Services.AddSingleton(sp => new ApplicationRegistryClient(
			sp.GetRequiredService<IHttpClientFactory>(),
			NullLogger<ApplicationRegistryClient>.Instance));

		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);
		var (success, error, _) = await auth.LoginAsync("headwiz", "password");
		if (!success)
			throw new InvalidOperationException($"Test setup login failed: {error}");

		Services.AddSingleton(auth);
		return auth;
	}

	private IRenderedComponent<MainLayout> RenderMainLayout() => Render<MainLayout>();

	/// <summary>Opens the account menu and clicks the "Beta" entry — the exact user gesture that
	/// invokes <c>MainLayout.SwitchCharacterAsync</c> via <c>AccountChrome.OnSwitchCharacter</c>.</summary>
	/// <summary>
	/// Each character row renders as its own <c>div.mud-menu-item</c> (MudBlazor's <c>MudMenuItem</c>),
	/// which is the element carrying the actual <c>@onclick</c> handler — ancestor wrapper divs (the
	/// popover provider, the popover surface, the listbox) all contain "Beta (#2)" too via their
	/// aggregated text, so a broad "any element containing this text" search would land on one of
	/// those non-interactive ancestors instead.
	/// </summary>
	private static void ClickSwitchToBeta(IRenderedComponent<MainLayout> cut)
	{
		cut.Find("button.phosphor-user-btn").Click();
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Beta (#2)"))
				throw new InvalidOperationException("menu not open yet");
		});
		cut.FindAll("div.mud-menu-item").First(e => e.TextContent.Contains("Beta (#2)")).Click();
	}

	[Test]
	public async Task Switching_recreates_both_terminals()
	{
		var terminal = RegisterTerminal();
		var playTerminal = RegisterPlayTerminal();
		await CreateLoggedInAuthAsync();

		var cut = RenderMainLayout();

		cut.InvokeAsync(() => ClickSwitchToBeta(cut));
		cut.WaitForAssertion(() => terminal.First.Received(1).DisposeAsync());

		// Both the command terminal AND the play terminal recreated — nothing in the codebase ever
		// disconnected the play terminal on switch before this, so it stayed logged in as the old
		// character indefinitely.
		await terminal.First.Received(1).DisposeAsync();
		await playTerminal.First.Received(1).DisposeAsync();
	}

	[Test]
	public async Task Switching_opens_no_terminal_window()
	{
		var terminal = RegisterTerminal();
		RegisterPlayTerminal();
		await CreateLoggedInAuthAsync();

		var cut = RenderMainLayout();
		await Assert.That(cut.Markup).DoesNotContain("phosphor-terminal-header")
			.Because("the drawer must not be open before switching either");

		cut.InvokeAsync(() => ClickSwitchToBeta(cut));
		// Wait for the async switch (mint OTT -> recreate x2 -> connect) to fully settle.
		cut.WaitForAssertion(() => terminal.First.Received(1).DisposeAsync());

		// Switching connects in the background; the toolbar toggle is the only thing that opens
		// the docked terminal drawer.
		await Assert.That(cut.Markup).DoesNotContain("phosphor-terminal-header");
	}

	[Test]
	public async Task Switching_sets_ActiveCharacter_even_if_the_connect_fails()
	{
		var terminal = RegisterTerminal();
		RegisterPlayTerminal();
		// The post-recreate inner never reports a successful connection — ConnectWithOttAsync
		// completes (no exception; a real network failure surfaces asynchronously via
		// ConnectionStateChanged, not a thrown exception) but IsConnected stays false throughout,
		// simulating a failed auto-login.
		terminal.Second.IsConnected.Returns(false);
		var auth = await CreateLoggedInAuthAsync();

		var cut = RenderMainLayout();

		cut.InvokeAsync(() => ClickSwitchToBeta(cut));
		cut.WaitForAssertion(() => terminal.Second.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "new-character-ott"));

		// Identity commits regardless of whether the connection succeeds; a failed auto-login
		// surfaces as a terminal error with a retry, not a rollback.
		await Assert.That(auth.ActiveCharacter?.DbrefNumber).IsEqualTo(2);
		await Assert.That(auth.ActiveCharacter?.Name).IsEqualTo("Beta");
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
