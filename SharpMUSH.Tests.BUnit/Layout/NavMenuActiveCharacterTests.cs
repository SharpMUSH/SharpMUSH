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
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// Fakes the two endpoints NavMenu's own init touches: <c>api/auth/account-login</c> (used here to
/// seed a real <see cref="AccountAuthService"/> with a two-character roster in server order — the
/// same pattern as <c>AccountAuthServiceActiveCharacterTests.FakeLoginHandler</c>) and
/// <c>api/applications</c> (fetched unconditionally by <see cref="ApplicationRegistryClient"/> from
/// NavMenu's own <c>OnInitializedAsync</c>).
/// </summary>
file sealed class NavMenuApiHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
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

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

file sealed class NavMenuStubLocalizer<T> : IStringLocalizer<T>
{
	public LocalizedString this[string name] => new(name, name);
	public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
	public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Regression coverage for the reported bug: switching characters in the portal updates the
/// top-right chrome but the bottom-left nav profile card kept showing the old name. Two compounding
/// causes, both exercised here: (1) DisplayName/AvatarInitial/UserTag read
/// <c>AccountAuth.Characters.FirstOrDefault()</c> — the roster in server order, which switching
/// never reorders, so it was a frozen constant; (2) NavMenu is a sibling of MainLayout, not a child,
/// so no parameter flow ever reaches it — it must subscribe to
/// <see cref="AccountAuthService.ActiveCharacterChanged"/> directly.
/// </summary>
public class NavMenuActiveCharacterTests : BunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];
	private BunitAuthorizationContext Auth { get; }

	public NavMenuActiveCharacterTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, NavMenuStubLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;

		// Wrapped in the real facades (not bare substitutes) — NavMenu now also injects
		// CharacterSwitchService, which needs the concrete TerminalServiceHost/PlayTerminalServiceHost
		// types to depend on. None of these tests trigger an actual switch.
		var terminal = Substitute.For<ITerminalService>();
		terminal.IsConnected.Returns(false);
		var terminalHost = new TerminalServiceHost(() => terminal);
		Services.AddSingleton(terminalHost);
		Services.AddSingleton<ITerminalService>(terminalHost);

		var playTerminal = Substitute.For<IPlayTerminalService>();
		playTerminal.IsConnected.Returns(false);
		var playTerminalHost = new PlayTerminalServiceHost(() => playTerminal);
		Services.AddSingleton(playTerminalHost);
		Services.AddSingleton<IPlayTerminalService>(playTerminalHost);

		Services.AddSingleton(NSubstitute.Substitute.For<SharpMUSH.Library.Services.Interfaces.IConnectionStateService>());
		Services.AddSingleton<CharacterSwitchService>();

		Auth = this.AddAuthorization();
		Auth.SetAuthorized("headwiz");
	}

	/// <summary>
	/// Builds a real <see cref="AccountAuthService"/> (its members aren't virtual, so it can't be
	/// NSubstitute-faked — see AccountPageTests) and logs it in against
	/// <see cref="NavMenuApiHandler"/>, which seeds the roster [Alpha(#1), Beta(#2)] in that server
	/// order. <c>AccountAuthService.SetCharacters</c> defaults <c>ActiveCharacter</c> to the roster's
	/// first entry (Alpha) when nothing is active yet — the constant the old FirstOrDefault() bug
	/// always rendered, regardless of any later switch.
	/// </summary>
	private async Task<AccountAuthService> CreateLoggedInAuthAsync()
	{
		var characters = new[]
		{
			new CharacterSummary(1, 1L, "Alpha", ""),
			new CharacterSummary(2, 2L, "Beta", ""),
		};

		var apiClient = new HttpClient(new NavMenuApiHandler(characters)) { BaseAddress = new Uri("https://localhost:8081/") };
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

		return auth;
	}

	private IRenderedComponent<NavMenu> RenderNavMenu(AccountAuthService auth, bool isCollapsed)
	{
		Services.AddSingleton(auth);
		return Render<NavMenu>(p => p.Add(c => c.IsCollapsed, isCollapsed));
	}

	[Test]
	public async Task DisplayName_tracks_ActiveCharacter_not_roster_order()
	{
		// The regression test for the reported bug: the roster is in server order and never
		// reorders on switch, so FirstOrDefault() was a constant.
		var auth = await CreateLoggedInAuthAsync();
		auth.SetActiveCharacter(new CharacterSummary(2, 2L, "Beta", ""));

		var cut = RenderNavMenu(auth, isCollapsed: false);

		await Assert.That(cut.Find(".phosphor-profile-name").TextContent).IsEqualTo("Beta");
	}

	[Test]
	public async Task Card_updates_when_ActiveCharacterChanged_fires_with_no_parent_rerender()
	{
		var auth = await CreateLoggedInAuthAsync();
		var cut = RenderNavMenu(auth, isCollapsed: false);
		await Assert.That(cut.Find(".phosphor-profile-name").TextContent).IsEqualTo("Alpha");

		// No parameter change, no parent render — exactly the sibling-component situation
		// that left the card stale.
		await cut.InvokeAsync(() => auth.SetActiveCharacter(new CharacterSummary(2, 2L, "Beta", "")));

		await cut.WaitForAssertionAsync(() =>
		{
			if (cut.Find(".phosphor-profile-name").TextContent != "Beta")
				throw new InvalidOperationException("card not updated yet");
		});
		await Assert.That(cut.Find(".phosphor-profile-name").TextContent).IsEqualTo("Beta");
		await Assert.That(cut.Find(".phosphor-profile-sub").TextContent).Contains("#2");
	}

	[Test]
	public async Task Avatar_initial_tracks_ActiveCharacter()
	{
		var auth = await CreateLoggedInAuthAsync();
		auth.SetActiveCharacter(new CharacterSummary(2, 2L, "Beta", ""));

		var cut = RenderNavMenu(auth, isCollapsed: false);

		await Assert.That(cut.Find(".phosphor-avatar").TextContent.Trim()).IsEqualTo("B");
	}

	/// <summary>
	/// Disposes the HttpClient(s) created for the substitute IHttpClientFactory. TUnit's disposer
	/// prefers IAsyncDisposable over IDisposable when a type implements both (as BunitContext
	/// does), so overriding only Dispose would never run — re-declare DisposeAsync to take over
	/// the interface's dispatch slot for this type; base.DisposeAsync() still runs to dispose
	/// bUnit's own service provider.
	/// </summary>
	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
