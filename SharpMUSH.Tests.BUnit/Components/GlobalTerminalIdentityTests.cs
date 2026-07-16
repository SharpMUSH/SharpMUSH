using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components;
using SharpMUSH.Client.Services;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Fakes the two endpoints this flow touches: <c>api/auth/account-login</c> (seeds a real
/// <see cref="AccountAuthService"/> with a two-character roster) and <c>api/auth/switch-character</c>
/// (the character picker's connect action).
/// </summary>
file sealed class GlobalTerminalApiHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
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

/// <summary>
/// Task 7 removed <c>CharacterPicker</c>'s own write to <c>Terminal.ConnectedPlayerName</c> —
/// <c>AccountAuth.ActiveCharacter</c> became the sole owner of identity — but left
/// <c>GlobalTerminal.OnCharacterPickerConnected</c> reading the now-dead
/// <c>Terminal.ConnectedPlayerName</c> back off the freshly-recreated (and never written-to) inner
/// terminal. The connection bar then rendered "not logged in" for a character that was actually
/// selected and connecting. This pins the fix: <c>GlobalTerminal</c> must read
/// <c>AccountAuth.ActiveCharacter?.Name</c> instead, exactly like <c>MainLayout</c>'s readers.
/// </summary>
public class GlobalTerminalIdentityTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];

	private static readonly CharacterSummary Alpha = new(1, 1L, "Alpha", "");
	private static readonly CharacterSummary Beta = new(2, 2L, "Beta", "");

	public GlobalTerminalIdentityTests()
	{
		Services.AddMudServices();
		JSInterop.Mode = JSRuntimeMode.Loose;

		// GlobalTerminal.OnInitializedAsync calls AccountAuth.InitAsync() itself — the FIRST call to
		// it in this test (CreateLoggedInAuthAsync's LoginAsync doesn't call InitAsync), so it
		// genuinely re-hydrates from "sessionStorage". Without these, the unconfigured Loose getItem
		// reads return null and InitCoreAsync would wipe the session LoginAsync just set up (same
		// wrinkle documented in SwitchCharacterFlowTests).
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);

		// "Production" keeps GlobalTerminal out of its dev-only debug-OTT auto-connect branch, so the
		// only path it takes with a logged-in, multi-character account is the account/character-picker
		// path this test exercises.
		var hostEnv = Substitute.For<IWebAssemblyHostEnvironment>();
		hostEnv.Environment.Returns("Production");
		Services.AddSingleton(hostEnv);

		Services.AddSingleton(sp => new CredentialService(sp.GetRequiredService<IJSRuntime>()));
		Services.AddSingleton(sp => new OttAuthService(
			sp.GetRequiredService<IHttpClientFactory>(),
			NullLogger<OttAuthService>.Instance));
	}

	/// <summary>
	/// Registers a real <see cref="TerminalServiceHost"/> (concrete type AND its interface aliased to
	/// the same instance, mirroring production DI) backed by a two-substitute factory queue, so
	/// <c>GlobalTerminal</c>'s injected <c>DefaultTerminal</c> and <c>CharacterPicker</c>'s injected
	/// <c>Terminal</c> resolve to the exact same facade — required for the picker's
	/// <c>RecreateAsync()</c> to be visible to the terminal that renders the connection bar.
	/// </summary>
	private (ITerminalService First, ITerminalService Second) RegisterTerminal()
	{
		var first = Substitute.For<ITerminalService>();
		var second = Substitute.For<ITerminalService>();
		var queue = new Queue<ITerminalService>([first, second]);
		var host = new TerminalServiceHost(() => queue.Dequeue());
		Services.AddSingleton(host);
		Services.AddSingleton<ITerminalService>(host);
		return (first, second);
	}

	private async Task<AccountAuthService> CreateLoggedInAuthAsync()
	{
		var apiClient = new HttpClient(new GlobalTerminalApiHandler([Alpha, Beta])) { BaseAddress = new Uri("https://localhost:8081/") };
		_ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services.AddSingleton(factory);

		var auth = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance);
		var (success, error, _) = await auth.LoginAsync("headwiz", "password");
		if (!success)
			throw new InvalidOperationException($"Test setup login failed: {error}");

		Services.AddSingleton(auth);
		return auth;
	}

	/// <summary>
	/// The character picker's own "Connect" button — distinct from the connection bar's top-level
	/// "Connect" button (rendered whenever <c>!_connected</c>, which is throughout this flow), so the
	/// search is scoped to <c>.char-picker</c>.
	/// </summary>
	private static void ClickCharacterPickerConnect(IRenderedComponent<GlobalTerminal> cut)
	{
		cut.Find(".char-picker").QuerySelectorAll("button")
			.First(b => b.TextContent.Trim() == "Connect")
			.Click();
	}

	[Test]
	public async Task Connecting_via_character_picker_shows_the_active_character_not_a_dead_terminal_property()
	{
		var (_, second) = RegisterTerminal();
		var auth = await CreateLoggedInAuthAsync();

		var cut = Render<GlobalTerminal>();

		// Preconditions: logged in with >1 character routes GlobalTerminal into the character-picker
		// panel instead of auto-connecting or showing the plain login form. OnInitializedAsync's own
		// AccountAuth.InitAsync() call genuinely yields (Task.Yield()), so the picker doesn't appear
		// in the synchronous first render — wait for it rather than asserting immediately.
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("char-picker"))
				throw new InvalidOperationException("character picker not shown yet");
		});

		await cut.InvokeAsync(() => ClickCharacterPickerConnect(cut));

		// Confirms the picker's flow (RecreateAsync -> ConnectWithOttAsync on the fresh, second inner)
		// actually ran before asserting on its effect.
		cut.WaitForAssertion(() => second.Received(1).ConnectWithOttAsync(Arg.Any<string>(), "new-character-ott"));

		await Assert.That(auth.ActiveCharacter?.Name).IsEqualTo("Alpha");

		// Second never has its ConnectedPlayerName written (Task 7 removed that write), so the old
		// `_playerName = Terminal.ConnectedPlayerName` read would stay null forever here — simulate a
		// connect completing so the connection bar's playerName branch renders, and confirm it shows
		// the active character rather than "not logged in".
		await cut.InvokeAsync(() => second.ConnectionStateChanged += Raise.Event<Action<bool>>(true));

		await Assert.That(cut.Markup).Contains("Alpha");
		await Assert.That(cut.Markup).DoesNotContain("not logged in");
	}

	/// <summary>
	/// Fix pass 2, Finding 1. This is the MainLayout character-switch shape specifically (as opposed
	/// to the character-picker flow above): identity commits on <c>AccountAuth</c> first, then
	/// <c>TerminalServiceHost.RecreateAsync()</c> announces a genuine disconnect (dropping the old,
	/// now-stale-identity connection), then a fresh connect on the recreated inner announces
	/// <c>true</c> — <c>GlobalTerminal.OnConnectionChanged(true)</c> never itself restores identity,
	/// so this only passes if the connbar derives its label from <c>AccountAuth.ActiveCharacter</c>
	/// live rather than a field nulled by the disconnect announcement and never written back.
	/// </summary>
	[Test]
	public async Task Recreate_then_a_genuine_connect_shows_the_active_character_not_a_stale_null()
	{
		var (first, second) = RegisterTerminal();
		first.IsConnected.Returns(true);
		first.ConnectedPlayerName.Returns((string?)null);
		var auth = await CreateLoggedInAuthAsync();

		var cut = Render<GlobalTerminal>();

		// Preconditions: connected from the start (so init's own auto-connect/picker branches never
		// engage — this test is about the switch shape, not first-login), showing the roster default.
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Alpha"))
				throw new InvalidOperationException("not showing Alpha yet");
		});

		// The switch is authoritative for identity and commits before the terminal is touched at all
		// (mirrors MainLayout.SwitchCharacterAsync's own ordering).
		auth.SetActiveCharacter(Beta);

		var host = Services.GetRequiredService<TerminalServiceHost>();
		await cut.InvokeAsync(() => host.RecreateAsync());

		// Confirms the disconnect announcement actually reached the connbar before asserting on the
		// pre-fix symptom: at this exact point the OLD code had already nulled _playerName via
		// OnConnectionChanged(false) and nothing had restored it yet.
		cut.WaitForAssertion(() =>
		{
			if (cut.Markup.Contains("Connected"))
				throw new InvalidOperationException("recreate's disconnect announcement hasn't landed yet");
		});

		await cut.InvokeAsync(() => second.ConnectionStateChanged += Raise.Event<Action<bool>>(true));

		await Assert.That(cut.Markup).Contains("Beta");
		await Assert.That(cut.Markup).DoesNotContain("not logged in");
	}

	/// <summary>
	/// Fix pass 2, Finding 2. Identical bug to the one already fixed in NavMenu.razor, in a different
	/// file: <c>OnInitializedAsync</c> used to fall back to <c>AccountAuth.Characters[0]</c> — the
	/// roster's FIRST entry — whenever the dead <c>Terminal.ConnectedPlayerName</c> read came back
	/// null (which, after a recreate, it always does). Remounting the drawer after switching to Beta
	/// showed "Alpha" as a result. This renders GlobalTerminal fresh with the roster's active
	/// character already reassigned to Beta (simulating a switch that happened before this mount —
	/// the terminal itself is already connected, since the real connection is a singleton facade that
	/// survives remounts) and confirms the remount shows the ACTUALLY active character.
	/// </summary>
	[Test]
	public async Task Remount_with_a_non_default_active_character_shows_that_character_not_the_roster_first()
	{
		var (first, _) = RegisterTerminal();
		first.IsConnected.Returns(true);
		first.ConnectedPlayerName.Returns((string?)null);
		var auth = await CreateLoggedInAuthAsync();

		// Simulates a switch that already committed before this mount (e.g. the drawer was closed
		// during the switch and is now being reopened/remounted).
		auth.SetActiveCharacter(Beta);

		var cut = Render<GlobalTerminal>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Beta"))
				throw new InvalidOperationException("not showing Beta yet");
		});
		await Assert.That(cut.Markup).Contains("Beta");
		await Assert.That(cut.Markup).DoesNotContain("Alpha");
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
