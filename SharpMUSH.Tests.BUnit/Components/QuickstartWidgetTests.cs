using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models.Portal.Widgets;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Fakes the two endpoints the widget's seed path touches: <c>api/auth/account-login</c> (used to
/// log a real <see cref="AccountAuthService"/> in with a chosen roster, the same pattern as
/// <c>NavMenuActiveCharacterTests</c>) and <c>api/account/characters</c> (the load path
/// <see cref="AccountAuthService.GetCharactersAsync"/> hits when the widget hydrates).
/// </summary>
file sealed class QuickstartApiHandler(IReadOnlyList<CharacterSummary> characters) : HttpMessageHandler
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
					username = "newbie",
					characters,
					accountSessionToken = "session-token-1",
					mustChangePassword = false,
					role = (string?)null,
					permissions = Array.Empty<string>(),
				})
			});
		}

		if (request.Method == HttpMethod.Get && path == "api/account/characters")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(characters)
			});
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

public class QuickstartWidgetTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];

	public QuickstartWidgetTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private AccountAuthService BuildAuth(IReadOnlyList<CharacterSummary> characters)
	{
		var apiClient = Track(new HttpClient(new QuickstartApiHandler(characters)) { BaseAddress = new Uri("https://localhost:8081/") });
		_ownedHttpClients.Add(apiClient);
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);
		return new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());
	}

	private async Task<AccountAuthService> BuildLoggedInAuth(IReadOnlyList<CharacterSummary> characters)
	{
		var auth = BuildAuth(characters);
		// Mirror the real boot sequence: MainLayout primes InitAsync (single-flight) before any
		// login, so the widget's later GetCharactersAsync short-circuits on the cached hydration task
		// instead of re-reading the tab's (loose-JSInterop, non-persisting) sessionStorage and wiping
		// the in-memory session that LoginAsync just set.
		await auth.InitAsync();
		var (success, error, _) = await auth.LoginAsync("newbie", "password");
		if (!success)
			throw new InvalidOperationException($"Test setup login failed: {error}");
		return auth;
	}

	private IRenderedComponent<QuickstartWidget> RenderWidget(AccountAuthService auth)
	{
		Services.AddSingleton(auth);
		return Render<QuickstartWidget>(p => p
			.Add(c => c.Config, null)
			.Add(c => c.Zone, WidgetZone.RightSidebar.ToString()));
	}

	[TUnit.Core.Test]
	public async Task Anonymous_ShowsBrowseLinks_NoCreateHero()
	{
		var auth = BuildAuth([]);
		var cut = RenderWidget(auth);

		await Assert.That(cut.Markup).Contains("WidReadTheWiki");
		await Assert.That(cut.Markup).Contains("WidBrowseCharacters");
		await Assert.That(cut.Markup).DoesNotContain("WidCreateYourCharacter");
	}

	[TUnit.Core.Test]
	public async Task LoggedIn_ZeroCharacters_ShowsCreateHero()
	{
		var auth = await BuildLoggedInAuth([]);
		var cut = RenderWidget(auth);

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("WidCreateYourCharacter"))
				throw new InvalidOperationException("hero not rendered yet");
		});

		var cta = cut.Find("a[href=\"/characters/new\"]");
		await Assert.That(cta.TextContent).Contains("WidCreateYourCharacter");
	}

	[TUnit.Core.Test]
	public async Task LoggedIn_WithCharacters_ShowsQuietNewLink_NoHero()
	{
		var auth = await BuildLoggedInAuth([new CharacterSummary(1, 1L, "Alpha", "")]);
		var cut = RenderWidget(auth);

		var link = cut.Find("a[href=\"/characters/new\"]");
		await Assert.That(link.TextContent).Contains("WidNewCharacter");
		await Assert.That(cut.Markup).DoesNotContain("WidNoCharacterYet");
		await Assert.That(cut.Markup).DoesNotContain("WidCreateYourCharacter");
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
