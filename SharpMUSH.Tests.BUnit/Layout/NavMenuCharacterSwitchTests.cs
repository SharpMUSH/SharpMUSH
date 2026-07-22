using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
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
/// Fakes the endpoints NavMenu's own init and the switch flow touch — same shape as
/// <c>NewTabCharacterTests.NewTabApiHandler</c>, duplicated rather than shared because it is
/// file-scoped there.
/// </summary>
file sealed class NavMenuSwitchApiHandler(IReadOnlyList<CharacterSummary> characters, bool failSwitch = false) : HttpMessageHandler
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

		if (request.Method == HttpMethod.Post && path == "api/auth/switch-character")
		{
			// failSwitch simulates an expired account session: the server rejects the OTT mint, which
			// is what CharacterSwitchService.SwitchAsync surfaces as a false return.
			if (failSwitch)
			{
				return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
			}

			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new { ott = "new-character-ott", expiresIn = 300 })
			});
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

file sealed class NavMenuSwitchStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Regression coverage for the review finding on the nav account panel's switch row: it used to call
/// only <c>AccountAuth.SwitchCharacterAsync</c> (identity-only — the profile card renamed itself, but
/// the command/play terminals never reconnected, so the terminal kept acting as the OLD character and
/// the freshly-minted OTT was silently discarded). This exercises the actual user gesture — open the
/// panel, open the character submenu, click a row — through the real <see cref="NavMenu"/> render
/// tree; the nav panel is now the only switcher, so there is no separate topbar surface to compare
/// against.
/// </summary>
public class NavMenuCharacterSwitchTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];
	private IConnectionStateService _connection = null!;
	private BunitAuthorizationContext Auth { get; }

	private static readonly CharacterSummary Alpha = new(1, 1L, "Alpha", "");
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	public NavMenuCharacterSwitchTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, NavMenuSwitchStubLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);

		Auth = this.AddAuthorization();
		Auth.SetAuthorized("headwiz");
	}

	private sealed record TerminalRig(ITerminalService First, ITerminalService Second);
	private sealed record PlayTerminalRig(IPlayTerminalService First);

	/// <summary>Real TerminalServiceHost (concrete type AND interface aliased to the same instance,
	/// mirroring <c>TerminalServiceCollectionExtensions.AddTerminalServices</c>), backed by a
	/// two-substitute factory queue so <c>RecreateAsync()</c> is observable via the first
	/// substitute's <c>DisposeAsync</c>.</summary>
	private TerminalRig RegisterTerminal()
	{
		var first = Substitute.For<ITerminalService>();
		var second = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, second]);
		var host = new TerminalServiceHost(() => queue.Dequeue());
		Services.AddSingleton(host);
		Services.AddSingleton<ITerminalService>(host);
		return new TerminalRig(first, second);
	}

	private PlayTerminalRig RegisterPlayTerminal()
	{
		var first = Substitute.For<IPlayTerminalService>();
		var second = Substitute.For<IPlayTerminalService>();
		var queue = new Queue<IPlayTerminalService>([first, second]);
		var host = new PlayTerminalServiceHost(() => queue.Dequeue());
		Services.AddSingleton(host);
		Services.AddSingleton<IPlayTerminalService>(host);
		return new PlayTerminalRig(first);
	}

	private async Task<AccountAuthService> CreateLoggedInAuthAsync(bool failSwitch = false)
	{
		var apiClient = new HttpClient(new NavMenuSwitchApiHandler([Alpha, Beta], failSwitch)) { BaseAddress = new Uri("https://localhost:8081/") };
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
		_connection = Substitute.For<IConnectionStateService>();
		Services.AddSingleton(_connection);
		Services.AddSingleton<CharacterSwitchService>();
		return auth;
	}

	/// <summary>Opens the account panel, opens the character submenu, and clicks the "Beta" row —
	/// the exact user gesture that should invoke <c>CharacterSwitchService.SwitchAsync</c> via
	/// <c>NavMenu.HandleSwitchCharacterAsync</c>.</summary>
	private static void ClickSwitchToBetaViaPanel(IRenderedComponent<MudHarness> cut)
	{
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
		cut.FindAll(".account-panel-character").Single(r => r.TextContent.Contains("Beta")).Click();
	}

	private IRenderedComponent<MudHarness> RenderNavMenu(bool isCollapsed = false)
		=> Render<MudHarness>(p => p.AddChildContent<NavMenu>(nm => nm.Add(c => c.IsCollapsed, isCollapsed)));

	[Test]
	public async Task Switching_from_the_panel_does_not_touch_the_terminals()
	{
		var terminal = RegisterTerminal();
		var playTerminal = RegisterPlayTerminal();
		var auth = await CreateLoggedInAuthAsync();

		var cut = RenderNavMenu();

		await cut.InvokeAsync(() => ClickSwitchToBetaViaPanel(cut));
		cut.WaitForAssertion(() =>
		{
			if (auth.ActiveCharacter?.DbrefNumber != 2)
				throw new InvalidOperationException("switch not applied yet");
		});

		// The account-panel switch is portal-only: a terminal's character is fixed at connect.
		await terminal.First.DidNotReceive().DisposeAsync();
		await playTerminal.First.DidNotReceive().DisposeAsync();
	}

	[Test]
	public async Task Switching_from_the_panel_reconnects_the_game_hub()
	{
		RegisterTerminal();
		RegisterPlayTerminal();
		await CreateLoggedInAuthAsync();

		var cut = RenderNavMenu();

		await cut.InvokeAsync(() => ClickSwitchToBetaViaPanel(cut));

		cut.WaitForAssertion(() => _connection.Received(1).ReconnectAsync());
	}

	[Test]
	public async Task Switching_from_the_panel_still_commits_ActiveCharacter()
	{
		RegisterTerminal();
		RegisterPlayTerminal();
		var auth = await CreateLoggedInAuthAsync();

		var cut = RenderNavMenu();

		await cut.InvokeAsync(() => ClickSwitchToBetaViaPanel(cut));

		cut.WaitForAssertion(() =>
		{
			if (auth.ActiveCharacter?.DbrefNumber != 2)
				throw new InvalidOperationException("identity not committed yet");
		});
		await Assert.That(auth.ActiveCharacter?.Name).IsEqualTo("Beta");
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
