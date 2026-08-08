using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Fakes the one endpoint /play's own roster check touches: <c>api/account/characters</c>. The
/// roster is configurable so a test can be an account that owns characters or one that owns none —
/// which is the whole distinction the page's empty state turns on. <paramref name="failFirstCalls"/>
/// errors that many roster requests before answering, which is the third distinction: an account
/// whose roster could not be fetched at all.
/// </summary>
file sealed class PlayPageApiHandler(IReadOnlyList<CharacterSummary> characters, int failFirstCalls = 0) : HttpMessageHandler
{
	private int _rosterCalls;

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Get && path == "api/account/characters")
			return Task.FromResult(_rosterCalls++ < failFirstCalls
				? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
				: new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(characters) });

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// Covers /play's three-state gate. The reported defect: an account holding ZERO characters got the
/// terminal anyway — blank, permanently "not connected", with a Connect button that could never
/// succeed and no explanation, while an anonymous visitor on the same route at least got the game's
/// "no guest characters available" line. That account now gets the same treatment /mail already
/// gives it (say what is missing, offer the action that fixes it), and the terminal is not mounted
/// at all in that state.
/// </summary>
public class PlayPageTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];

	public PlayPageTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<ServerInfoService>(new StubServerInfoService(guestsEnabled: true));
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		// "Production" keeps GlobalTerminal (rendered in the has-character/anonymous branches) out of
		// its dev-only debug-OTT auto-connect path, which would otherwise fire on every render here.
		var hostEnv = Substitute.For<IWebAssemblyHostEnvironment>();
		hostEnv.Environment.Returns("Production");
		Services.AddSingleton(hostEnv);

		var playTerminal = Substitute.For<IPlayTerminalService>();
		playTerminal.IsConnected.Returns(false);
		playTerminal.OobChannels.Returns(Substitute.For<IOobChannelStore>());
		var playHost = new PlayTerminalServiceHost(() => playTerminal);
		Services.AddSingleton(playHost);
		Services.AddSingleton<IPlayTerminalService>(playHost);

		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(false);
		var terminalHost = new TerminalServiceHost(() => terminal);
		Services.AddSingleton(terminalHost);
		Services.AddSingleton<ITerminalService>(terminalHost);
	}

	/// <summary>
	/// Registers a real <see cref="AccountAuthService"/> (its members aren't virtual, so it can't be
	/// NSubstitute-faked — see AccountPageTests) whose <c>InitAsync</c> rehydrates the session from
	/// the seeded "sessionStorage", and whose roster fetch returns <paramref name="characters"/>.
	/// Seeding a session token is what makes <c>IsLoggedIn</c> true without a login round-trip.
	/// </summary>
	private void SeedAccount(bool loggedIn, IReadOnlyList<CharacterSummary> characters, int rosterFailures = 0)
	{
		var apiClient = new HttpClient(new PlayPageApiHandler(characters, rosterFailures)) { BaseAddress = new Uri("https://localhost:8081/") };
		_ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		Services.AddSingleton(factory);

		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken")
			.SetResult(loggedIn ? "session-token-1" : null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Player");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[]");

		Services.AddSingleton(new AccountAuthService(
			factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>()));
	}

	[Test]
	public async Task Signed_in_with_no_character_gets_an_explanation_and_a_way_to_create_one()
	{
		SeedAccount(loggedIn: true, characters: []);

		// Play hosts a MudMenu (terminal settings), which requires a MudPopoverProvider in the
		// render tree — MudHarness supplies one and contributes nothing else to the markup.
		var cut = Render<MudHarness>(p => p.AddChildContent<SharpMUSH.Client.Pages.Play>());
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("NavPlayNeedsCharacter"))
				throw new InvalidOperationException("roster not resolved yet");
		});

		await Assert.That(cut.Markup).Contains("NavPlayNeedsCharacter");
		await Assert.That(cut.Find("a.mud-button-root").GetAttribute("href")).IsEqualTo("/characters/new");
	}

	[Test]
	public async Task Signed_in_with_no_character_does_not_render_a_terminal_or_a_dead_Connect_button()
	{
		SeedAccount(loggedIn: true, characters: []);

		// Play hosts a MudMenu (terminal settings), which requires a MudPopoverProvider in the
		// render tree — MudHarness supplies one and contributes nothing else to the markup.
		var cut = Render<MudHarness>(p => p.AddChildContent<SharpMUSH.Client.Pages.Play>());
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("NavPlayNeedsCharacter"))
				throw new InvalidOperationException("roster not resolved yet");
		});

		// The whole point: no terminal shell, and therefore none of GlobalTerminal's connbar —
		// which is where the Connect button that could never succeed used to live.
		await Assert.That(cut.FindAll(".play-shell")).IsEmpty();
		await Assert.That(cut.FindAll(".sharp-terminal-container")).IsEmpty();
	}

	[Test]
	public async Task Signed_in_with_a_character_still_gets_the_terminal()
	{
		SeedAccount(loggedIn: true, characters: [new CharacterSummary(1, 1L, "Alpha", "")]);

		// Play hosts a MudMenu (terminal settings), which requires a MudPopoverProvider in the
		// render tree — MudHarness supplies one and contributes nothing else to the markup.
		var cut = Render<MudHarness>(p => p.AddChildContent<SharpMUSH.Client.Pages.Play>());
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".play-shell").Count == 0)
				throw new InvalidOperationException("terminal shell not rendered yet");
		});

		await Assert.That(cut.Markup).DoesNotContain("NavPlayNeedsCharacter");
		await Assert.That(cut.FindAll(".sharp-terminal-container")).IsNotEmpty();
	}

	[Test]
	public async Task Anonymous_visitor_still_gets_the_terminal()
	{
		// The guest path is GlobalTerminal's business (it connects as a game guest, or the game
		// tells the visitor there are none) — the gate must not swallow it.
		SeedAccount(loggedIn: false, characters: []);

		// Play hosts a MudMenu (terminal settings), which requires a MudPopoverProvider in the
		// render tree — MudHarness supplies one and contributes nothing else to the markup.
		var cut = Render<MudHarness>(p => p.AddChildContent<SharpMUSH.Client.Pages.Play>());
		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".play-shell").Count == 0)
				throw new InvalidOperationException("terminal shell not rendered yet");
		});

		await Assert.That(cut.Markup).DoesNotContain("NavPlayNeedsCharacter");
		await Assert.That(cut.FindAll(".sharp-terminal-container")).IsNotEmpty();
	}

	/// <summary>
	/// "You have no character, create one" is an assertion about the account. A roster request that
	/// never came back is not evidence for it — the service used to degrade a failed fetch to an
	/// empty list, so a game that was merely unreachable told a player with four characters that
	/// they had none.
	/// </summary>
	[Test]
	public async Task A_failed_roster_request_is_not_reported_as_having_no_character()
	{
		SeedAccount(loggedIn: true, characters: [new CharacterSummary(1, 1L, "Alpha", "", IsActing: true)],
			rosterFailures: int.MaxValue);

		var cut = Render<MudHarness>(p => p.AddChildContent<SharpMUSH.Client.Pages.Play>());
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("NavPlayRosterUnavailable"))
				throw new InvalidOperationException("failure state not rendered yet");
		});

		await Assert.That(cut.Markup).DoesNotContain("NavPlayNeedsCharacter");
		await Assert.That(cut.FindAll(".play-shell")).IsEmpty();
		await Assert.That(cut.Find("button.mud-button-root").TextContent).Contains("NavTryAgain");
	}

	[Test]
	public async Task Retrying_after_the_roster_request_recovers_mounts_the_terminal()
	{
		SeedAccount(loggedIn: true, characters: [new CharacterSummary(1, 1L, "Alpha", "", IsActing: true)],
			rosterFailures: 1);

		var cut = Render<MudHarness>(p => p.AddChildContent<SharpMUSH.Client.Pages.Play>());
		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("NavPlayRosterUnavailable"))
				throw new InvalidOperationException("failure state not rendered yet");
		});

		cut.Find("button.mud-button-root").Click();

		cut.WaitForAssertion(() =>
		{
			if (cut.FindAll(".play-shell").Count == 0)
				throw new InvalidOperationException("terminal shell not rendered yet");
		});

		await Assert.That(cut.Markup).DoesNotContain("NavPlayRosterUnavailable");
		await Assert.That(cut.FindAll(".sharp-terminal-container")).IsNotEmpty();
	}

	/// <summary>
	/// Disposes the HttpClient(s) created for the substitute IHttpClientFactory. TUnit's disposer
	/// prefers IAsyncDisposable over IDisposable when a type implements both (as BunitContext does),
	/// so overriding only Dispose would never run — re-declare DisposeAsync to take over the
	/// interface's dispatch slot for this type; base.DisposeAsync() still disposes bUnit's own
	/// service provider.
	/// </summary>
	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
