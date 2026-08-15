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
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// Fakes account-login/register and the character roster. <paramref name="characters"/> is what the
/// account owns: an empty roster is the "registered, no character yet" state.
/// </summary>
file sealed class OnboardingApiHandler(object[] characters) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');

		if (request.Method == HttpMethod.Post && path is "api/auth/account-login" or "api/auth/account-register")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new
				{
					accountId = "test-account-id",
					username = "newbie",
					characters,
					accountSessionToken = "test-session-token",
					mustChangePassword = false,
					role = "Guest",
					permissions = Array.Empty<string>()
				})
			});

		if (request.Method == HttpMethod.Get && path == "api/account/characters")
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(characters) });

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// "Registered, but no character yet" is a step in onboarding, not a permission failure. Cover for
/// the routing that treats it as one: login/registration with an empty roster lands on
/// <c>/characters/new</c>, which explains why, while an account that owns a character is unaffected.
/// </summary>
/// <remarks>
/// These assert routing and rendering directly rather than going through <c>[Authorize]</c>: bUnit
/// renders a page component BELOW <c>AuthorizeRouteView</c>, so the attribute has no runtime effect
/// here and a "render anonymously, expect a redirect" test would pass while proving nothing.
/// </remarks>
public class OnboardingRoutingTests : TrackingBunitContext, IAsyncDisposable
{
	private readonly List<HttpClient> _ownedHttpClients = [];

	private static readonly object[] OneCharacter =
	[
		new { dbrefNumber = 5, creationTime = 5L, name = "Gwendolyn", flags = "", isActing = true }
	];

	private void SeedServices(object[] characters)
	{
		var apiClient = Track(new HttpClient(new OnboardingApiHandler(characters)) { BaseAddress = new Uri("https://localhost:8081/") });
		_ownedHttpClients.Add(apiClient);

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>()))
			.AddSingleton(Substitute.For<ITerminalService>())
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();

		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	private BunitNavigationManager Nav => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

	private IRenderedComponent<SharpMUSH.Client.Pages.Login> SubmitLoginFrom(string startingUri, object[] characters)
	{
		SeedServices(characters);
		Nav.NavigateTo(startingUri);

		var cut = Render<SharpMUSH.Client.Pages.Login>();
		cut.Find("#login-username").Change("newbie");
		cut.Find("#login-password").Change("hunter2");
		cut.Find("button.login-submit").Click();
		return cut;
	}

	private void WaitForNavigation(IRenderedComponent<IComponent> cut, string relativeUrl)
	{
		var expected = new Uri(new Uri(Nav.BaseUri), relativeUrl).ToString();
		cut.WaitForAssertion(() =>
		{
			if (Nav.Uri != expected)
				throw new InvalidOperationException($"expected {expected}, still at {Nav.Uri}");
		});
	}

	[TUnit.Core.Test]
	public async Task Login_WithNoCharacters_LandsOnCharacterCreation()
	{
		var cut = SubmitLoginFrom("/login", characters: []);

		WaitForNavigation(cut, "/characters/new");

		await Assert.That(Nav.Uri).EndsWith("/characters/new");
	}

	/// <summary>
	/// Onboarding wins over the return URL: the gated page the visitor was heading for would refuse
	/// them anyway, and refusing without explanation is the state this replaces.
	/// </summary>
	[TUnit.Core.Test]
	public async Task Login_WithNoCharacters_PrefersOnboardingOverReturnUrl()
	{
		var cut = SubmitLoginFrom($"/login?returnUrl={Uri.EscapeDataString("/wiki")}", characters: []);

		WaitForNavigation(cut, "/characters/new");

		await Assert.That(Nav.Uri).EndsWith("/characters/new");
	}

	[TUnit.Core.Test]
	public async Task Login_WithACharacter_StillHonoursTheReturnUrl()
	{
		var cut = SubmitLoginFrom($"/login?returnUrl={Uri.EscapeDataString("/wiki")}", OneCharacter);

		WaitForNavigation(cut, "/wiki");

		await Assert.That(Nav.Uri).EndsWith("/wiki");
	}

	[TUnit.Core.Test]
	public async Task Registration_AlwaysContinuesIntoOnboarding()
	{
		SeedServices(characters: []);
		Nav.NavigateTo("/login?tab=register");

		var cut = Render<SharpMUSH.Client.Pages.Login>();
		var inputs = cut.FindAll("input");
		inputs[0].Change("newbie");
		inputs[2].Change("hunter2");
		cut.Find("button.register-submit").Click();

		WaitForNavigation(cut, "/characters/new");

		await Assert.That(Nav.Uri).EndsWith("/characters/new");
	}

	[TUnit.Core.Test]
	public async Task CharacterCreate_WithNoCharacters_ExplainsWhyYouAreHere()
	{
		SeedServices(characters: []);
		SeedSession();

		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();

		cut.WaitForAssertion(() => cut.Find("#onboarding-notice"));
		await Assert.That(cut.Markup).Contains("OnboardingCreateFirstCharacter");
	}

	/// <summary>An account that already plays someone is adding a character, not being onboarded.</summary>
	[TUnit.Core.Test]
	public async Task CharacterCreate_WithACharacter_ShowsNoOnboardingNotice()
	{
		SeedServices(OneCharacter);
		SeedSession();

		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("NavCreateACharacter", StringComparison.Ordinal))
				throw new InvalidOperationException("the create form has not rendered yet");
		});
		await Assert.That(cut.FindAll("#onboarding-notice")).IsEmpty();
	}

	private void SeedSession()
	{
		Services.AddSingleton(Substitute.For<ICharacterUpgradeService>());
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("newbie");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Player");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[]");
	}

	public new async ValueTask DisposeAsync()
	{
		foreach (var client in _ownedHttpClients)
			client.Dispose();
		await base.DisposeAsync();
	}
}
