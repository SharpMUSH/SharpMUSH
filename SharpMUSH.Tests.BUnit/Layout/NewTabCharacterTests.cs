using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Components;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// Fakes the endpoints touched by both halves of this feature: NavMenu/MainLayout init
/// (<c>api/auth/account-login</c>, <c>api/applications</c>, <c>api/setup/status</c>) and the switch
/// flow the consumer half drives (<c>api/auth/switch-character</c>) — same shape as
/// <c>NavMenuCharacterSwitchTests.NavMenuSwitchApiHandler</c>, duplicated rather than shared because
/// it is file-scoped there.
/// </summary>
file sealed class NewTabApiHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
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

file sealed class NewTabStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Task 9: opening a character in a new tab. Two halves, exercised through two different real render
/// trees:
/// - The OPENER (<see cref="NavMenu"/> wiring <c>AccountPanel.OnOpenInNewTab</c> to
///   <c>window.open</c>) — a new tab inherits a COPY of the opener's sessionStorage (including the
///   opener's OWN active character, not the one clicked), so the target must be passed explicitly as a
///   <c>?as=&lt;dbref&gt;-&lt;creationTime&gt;</c> entry hint.
/// - The CONSUMER (<see cref="MainLayout"/> parsing <c>?as=</c> on load, stripping it, and switching
///   via <see cref="CharacterSwitchService"/>) — the roster is the sole authority for whether a hint is
///   honored; a hint naming a character this account doesn't own, or a malformed hint, is silently
///   ignored, since the server would reject an unowned switch anyway.
///
/// DI wiring mirrors <c>NavMenuCharacterSwitchTests</c> (proven to render <see cref="NavMenu"/>
/// standalone); rendering <see cref="MainLayout"/> itself is proven directly by the consumer tests
/// below.
/// </summary>
public class NewTabCharacterTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];
	private BunitAuthorizationContext Auth { get; }

	private static readonly CharacterSummary Alpha = new(1, 1L, "Alpha", "");
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	public NewTabCharacterTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, NewTabStubLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		// MainLayout's OnInitializedAsync calls AccountAuth.InitAsync(), which is the FIRST call to it
		// in these tests — it genuinely re-hydrates from "sessionStorage". Without these, the
		// unconfigured Loose getItem calls return null and InitCoreAsync would wipe the session
		// LoginAsync just set up.
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

	private void RegisterTerminal()
	{
		var terminal = Substitute.For<ITerminalService>();
		var host = new TerminalServiceHost(() => terminal);
		Services.AddSingleton(host);
		Services.AddSingleton<ITerminalService>(host);
	}

	private void RegisterPlayTerminal()
	{
		var playTerminal = Substitute.For<IPlayTerminalService>();
		var host = new PlayTerminalServiceHost(() => playTerminal);
		Services.AddSingleton(host);
		Services.AddSingleton<IPlayTerminalService>(host);
	}

	private async Task<AccountAuthService> CreateLoggedInAuthAsync()
	{
		var apiClient = new HttpClient(new NewTabApiHandler([Alpha, Beta])) { BaseAddress = new Uri("https://localhost:8081/") };
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
		Services.AddSingleton<CharacterSwitchService>();
		return auth;
	}

	// ── Opener: NavMenu -> AccountPanel.OnOpenInNewTab -> window.open ──────────────────────────

	private IRenderedComponent<MudHarness> RenderNavMenu(bool isCollapsed = false)
		=> Render<MudHarness>(p => p.AddChildContent<NavMenu>(nm => nm.Add(c => c.IsCollapsed, isCollapsed)));

	[Test]
	public async Task OnOpenInNewTab_calls_window_open_with_the_as_hint()
	{
		RegisterTerminal();
		RegisterPlayTerminal();
		await CreateLoggedInAuthAsync();

		// Beta is #2, CreationTime 2L: the URL must carry BOTH halves, not just the dbref.
		var openHandler = JSInterop.SetupVoid("open", "/?as=2-2", "_blank");

		var cut = RenderNavMenu();

		cut.Find("button.phosphor-profile-card").Click();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel-switch-btn").Count == 0)
				throw new InvalidOperationException("panel not open yet");
		});
		cut.Find(".account-panel-switch-btn").Click();
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".account-panel-character").Count == 0)
				throw new InvalidOperationException("submenu not open yet");
		});

		var betaRow = cut.FindAll(".account-panel-character").Single(r => r.TextContent.Contains("Beta"));
		var newTabButton = betaRow.QuerySelector(".account-panel-newtab")!;
		newTabButton.Click();

		await Assert.That(openHandler.Invocations.Count).IsEqualTo(1);
	}

	// ── Consumer: MainLayout parses ?as=, strips it, and switches via CharacterSwitchService ───

	private IRenderedComponent<MainLayout> RenderMainLayoutAt(string relativeUrl)
	{
		Services.GetRequiredService<NavigationManager>().NavigateTo(relativeUrl);
		return Render<MainLayout>();
	}

	[Test]
	public async Task An_as_hint_matching_a_character_sets_it_active()
	{
		RegisterTerminal();
		RegisterPlayTerminal();
		var auth = await CreateLoggedInAuthAsync();
		// SetCharacters defaults ActiveCharacter to the roster's first entry (Alpha) at login.
		await Assert.That(auth.ActiveCharacter?.DbrefNumber).IsEqualTo(1);

		var cut = RenderMainLayoutAt("/?as=2-2");

		cut.WaitForAssertion(() =>
		{
			if (auth.ActiveCharacter?.DbrefNumber != 2)
				throw new InvalidOperationException("hint not consumed yet");
		});
		await Assert.That(auth.ActiveCharacter?.Name).IsEqualTo("Beta");

		// MainLayout.SwitchCharacterAsync deliberately does NOT open the terminal drawer on a
		// background/new-tab switch — the toolbar toggle opens it when the player wants it.
		// phosphor-terminal-header appears in exactly one place repo-wide (MainLayout.razor, inside
		// the `@if (_terminalOpen)` block), so its absence here is the pin for that contract.
		await Assert.That(cut.Markup).DoesNotContain("phosphor-terminal-header");
	}

	[Test]
	public async Task An_as_hint_is_stripped_from_the_url_after_consumption()
	{
		RegisterTerminal();
		RegisterPlayTerminal();
		await CreateLoggedInAuthAsync();
		var nav = Services.GetRequiredService<NavigationManager>();

		var cut = RenderMainLayoutAt("/?as=2-2");

		cut.WaitForAssertion(() =>
		{
			if (nav.Uri.Contains("as="))
				throw new InvalidOperationException("hint not stripped yet");
		});
		await Assert.That(nav.Uri).IsEqualTo("http://localhost/");
	}

	[Test]
	public async Task An_as_hint_naming_an_unowned_character_is_ignored()
	{
		RegisterTerminal();
		RegisterPlayTerminal();
		var auth = await CreateLoggedInAuthAsync();
		var nav = Services.GetRequiredService<NavigationManager>();

		// #99 is not on this account's roster [Alpha(#1), Beta(#2)] — the roster is the authority, and
		// the server would reject the switch anyway.
		var cut = RenderMainLayoutAt("/?as=99-99");

		cut.WaitForAssertion(() =>
		{
			if (nav.Uri.Contains("as="))
				throw new InvalidOperationException("hint not stripped yet");
		});

		// Give any (wrongly-fired) switch a chance to land before asserting it didn't.
		await Task.Delay(50);
		await Assert.That(auth.ActiveCharacter?.DbrefNumber).IsEqualTo(1);
		await Assert.That(auth.ActiveCharacter?.Name).IsEqualTo("Alpha");
	}

	[Test]
	public async Task A_malformed_as_hint_is_ignored_without_throwing()
	{
		RegisterTerminal();
		RegisterPlayTerminal();
		var auth = await CreateLoggedInAuthAsync();
		var nav = Services.GetRequiredService<NavigationManager>();

		// Not "<dbref>-<creationTime>" at all — must not throw, and must leave identity untouched.
		var cut = RenderMainLayoutAt("/?as=not-a-valid-hint-at-all");

		cut.WaitForAssertion(() =>
		{
			if (nav.Uri.Contains("as="))
				throw new InvalidOperationException("hint not stripped yet");
		});

		await Task.Delay(50);
		await Assert.That(auth.ActiveCharacter?.DbrefNumber).IsEqualTo(1);
		await Assert.That(cut.Markup).IsNotEmpty();
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
