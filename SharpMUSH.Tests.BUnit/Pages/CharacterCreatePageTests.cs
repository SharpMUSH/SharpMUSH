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
using SharpMUSH.Library.Models.Portal.Onboarding;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Fakes the character-create endpoint (<c>POST api/account/characters</c>).</summary>
file sealed class CharacterCreateApiHandler(bool succeed, bool existingCharacter) : HttpMessageHandler
{
	public bool FailRoster { get; set; }
	public bool FailRosterAfterCreate { get; set; }
	public int Creates { get; private set; }
	public string? LastBody { get; private set; }
	/// <summary>Reads of the session's authority after a character was made.</summary>
	public int SessionReads { get; private set; }
	private string _createdName = "Bob";
	private int _createdId = 5;
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (request.Method == HttpMethod.Post && request.Content is not null)
			LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
		return await Respond(request, path);
	}

	private Task<HttpResponseMessage> Respond(HttpRequestMessage request, string path)
	{
		if (request.Method == HttpMethod.Get && path == "api/account/session" && Creates > 0) SessionReads++;
		if (request.Method == HttpMethod.Get && path == "api/account/characters")
			return Task.FromResult(FailRoster || (FailRosterAfterCreate && Creates > 0)
				? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
				: new HttpResponseMessage(HttpStatusCode.OK)
				{
					Content = JsonContent.Create(
					(existingCharacter ? new[] { new AccountAuthService.CharacterSummary(2, 2, "Existing", "") } : [])
					.Concat(Creates == 0 ? [] : new[] { new AccountAuthService.CharacterSummary(_createdId, _createdId, _createdName, "") }).ToArray())
				});
		if (request.Method == HttpMethod.Post && path is "api/account/characters" or "api/account/link-character")
		{
			Creates++;
			if (path.EndsWith("link-character")) { _createdName = "Imported Bob"; _createdId = 7; }
		}
		if (request.Method == HttpMethod.Post && path == "api/account/link-character")
			return Task.FromResult(succeed
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { dbrefNumber = 7, creationTime = 7L, name = "Imported Bob" }) }
				: new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Invalid character credentials.") });

		if (request.Method == HttpMethod.Post && path == "api/account/characters")
			return Task.FromResult(succeed
				? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { dbrefNumber = 5, creationTime = 5L }) }
				: new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("Name already taken.") });

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>Server info that answers only the onboarding presentation, which is what the create page reads.</summary>
internal sealed class GuideServerInfoService(IHttpClientFactory http, OnboardingPresentation? presentation = null) : ServerInfoService(http)
{
	public override Task<OnboardingPresentation?> OnboardingAsync() => Task.FromResult(presentation);
}

/// <summary>
/// Coverage for the dedicated create-character page (<c>/characters/new</c>): it renders the form,
/// and a successful create returns the player to <c>/account</c>. Character creation was split out of
/// the account page into its own surface.
/// </summary>
public class CharacterCreatePageTests : TrackingBunitContext, IAsyncDisposable
{
	private ICharacterUpgradeService _upgrade = null!;

	private (Action<bool> FailRoster, Action<bool> FailAfterCreate, Func<int> Creates, Func<string?> LastBody, Func<int> SessionReads) SeedLoggedIn(bool createSucceeds,
		bool existingCharacter = false, GuideLink? characterGuide = null)
	{
		this.AddAuthorization().SetAuthorized("headwiz");

		var handler = new CharacterCreateApiHandler(createSucceeds, existingCharacter);
		var apiClient = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(factory)
			.AddSingleton<NativeCharacterCredentialsClient>()
			.AddSingleton<ServerInfoService>(new GuideServerInfoService(factory, characterGuide is null ? null
				: new OnboardingPresentation("game", "", "", "", "", "", null, null, characterGuide)))
			.AddSingleton(sp => new AccountAuthService(
				sp.GetRequiredService<IHttpClientFactory>(),
				sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, []));

		_upgrade = Substitute.For<ICharacterUpgradeService>();
		_upgrade.PlayAsAsync(Arg.Any<AccountAuthService.CharacterSummary>()).Returns(Task.FromResult(true));
		Services.AddSingleton(_upgrade);

		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.mustChangePassword").SetResult(bool.FalseString);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("Wizard");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		return (value => handler.FailRoster = value, value => handler.FailRosterAfterCreate = value, () => handler.Creates, () => handler.LastBody,
			() => handler.SessionReads);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CreateAndClaimExplainAccountVersusCharacterWithoutChangingTheHandoff(bool claim)
	{
		SeedLoggedIn(true);
		Services.GetRequiredService<NavigationManager>().NavigateTo(claim ? "/characters/claim" : "/characters/new");
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await Assert.That(cut.Find(".journey-identity").TextContent).IsEqualTo("AuthJourneyIdentity");
		await Assert.That(cut.Find(".journey-character-links a[href='/characters']").TextContent).IsEqualTo("NavJourneyDirectory");
		await Assert.That(cut.Find(".journey-character-links a[href='/account']").TextContent).IsEqualTo("NavCharYourCharacters");
		if (claim)
			await Assert.That(cut.FindAll("label").Any(label => label.TextContent.Contains("AuthCharacterPassword", StringComparison.Ordinal))).IsTrue();
		else
			await Assert.That(cut.Find("a[href='/characters/claim']").TextContent).IsEqualTo("NavClaimInstead");
		await Assert.That(cut.FindAll("a[href='/account']")).IsNotEmpty();
		await Assert.That(cut.Find("h1").TextContent).IsEqualTo(claim ? "NavClaimACharacter" : "NavCreateACharacter");
	}

	private static Task ClickCreate(IRenderedComponent<SharpMUSH.Client.Pages.CharacterCreate> cut)
		=> cut.FindAll("button").First(b => b.TextContent.Trim() == "NavCreateCharacter").ClickAsync();

	[TUnit.Core.Test]
	[TUnit.Core.Arguments(false)]
	[TUnit.Core.Arguments(true)]
	public async Task Create_and_claim_have_a_page_level_heading(bool claim)
	{
		SeedLoggedIn(true);
		Services.GetRequiredService<NavigationManager>().NavigateTo(claim ? "/characters/claim" : "/characters/new");
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await Assert.That(cut.Find("h1").TextContent).IsEqualTo(claim ? "NavClaimACharacter" : "NavCreateACharacter");
	}

	[TUnit.Core.Test]
	[TUnit.Core.Arguments(false)]
	[TUnit.Core.Arguments(true)]
	public async Task Additional_character_does_not_switch_the_active_character(bool returnToGuide)
	{
		SeedLoggedIn(true, existingCharacter: true);
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo(returnToGuide ? "/characters/new?returnUrl=%2Fwelcome%3Fresume%3Dtrue" : "/characters/new");
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await cut.FindAll("input").First().ChangeAsync("Bob");
		await ClickCreate(cut);
		await Assert.That(nav.Uri).EndsWith(returnToGuide ? "/welcome?resume=true" : "/account");
		await _upgrade.DidNotReceive().PlayAsAsync(Arg.Any<AccountAuthService.CharacterSummary>());
	}

	[TUnit.Core.Test]
	public async Task Passwordless_creation_keeps_saved_character_when_roster_refresh_fails()
	{
		var api = SeedLoggedIn(true);
		api.FailAfterCreate(true);
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await cut.FindAll("input").First().ChangeAsync("Bob");
		await ClickCreate(cut);
		await Assert.That(cut.Markup).Contains("GuideCharacterRosterFailed");
		await Assert.That(api.Creates()).IsEqualTo(1);
		api.FailAfterCreate(false);
		await cut.FindAll("button").Single(b => b.TextContent.Trim() == "GuideRetryConnection").ClickAsync();
		await Assert.That(api.Creates()).IsEqualTo(1);
		await _upgrade.Received(1).PlayAsAsync(Arg.Any<AccountAuthService.CharacterSummary>());
	}

	[TUnit.Core.Test]
	public async Task Initial_roster_failure_offers_retry_before_character_creation()
	{
		var api = SeedLoggedIn(true);
		api.FailRoster(true);
		await Services.GetRequiredService<AccountAuthService>().InitAsync();
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await Assert.That(cut.FindAll("button").Single(b => b.TextContent.Trim() == "NavCreateCharacter").HasAttribute("disabled")).IsTrue();
		await cut.WaitForAssertionAsync(() =>
		{
			if (cut.FindAll("button").Single(b => b.TextContent.Trim() == "GuideRetryRoster").HasAttribute("disabled"))
				throw new InvalidOperationException("Roster request is still running");
		});
		api.FailRoster(false);
		await cut.InvokeAsync(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == "GuideRetryRoster").ClickAsync());
		await Assert.That(cut.FindAll("button").Single(b => b.TextContent.Trim() == "NavCreateCharacter").HasAttribute("disabled")).IsFalse();
	}

	[TUnit.Core.Test]
	public async Task Renders_the_create_character_form()
	{
		SeedLoggedIn(createSucceeds: true);

		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();

		await Assert.That(cut.Markup).Contains("NavCreateACharacter");
		await Assert.That(cut.FindAll("button").Any(b => b.TextContent.Trim() == "NavCreateCharacter")).IsTrue();
	}

	[TUnit.Core.Test]
	public async Task Creating_a_first_character_upgrades_and_goes_to_play()
	{
		SeedLoggedIn(createSucceeds: true);
		var nav = Services.GetRequiredService<NavigationManager>();

		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await cut.FindAll("input").First().ChangeAsync("Bob");

		await ClickCreate(cut);

		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/play"))
				throw new InvalidOperationException("did not navigate to /play yet");
		});
		await _upgrade.Received(1).PlayAsAsync(Arg.Is<AccountAuthService.CharacterSummary>(c => c.Name == "Bob"));
	}

	[TUnit.Core.Test]
	public async Task Claiming_a_first_character_links_it_and_goes_to_play()
	{
		SeedLoggedIn(createSucceeds: true);
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/characters/claim");

		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await Assert.That(cut.Markup).Contains("NavClaimACharacter");
		await cut.FindAll("input").First().ChangeAsync("imported bob");
		await cut.Find("input[type=password]").ChangeAsync("bob-secret");

		await cut.FindAll("button").First(b => b.TextContent.Trim() == "NavClaimCharacter").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/play"))
				throw new InvalidOperationException("did not navigate to /play yet");
		});
		// The roster takes the name as the game spells it, not as it was typed.
		await _upgrade.Received(1).PlayAsAsync(Arg.Is<AccountAuthService.CharacterSummary>(c => c.Name == "Imported Bob" && c.DbrefNumber == 7));
	}

	[TUnit.Core.Test]
	[TUnit.Core.Arguments(false)]
	[TUnit.Core.Arguments(true)]
	public async Task Connection_failure_preserves_created_character_and_retries_without_creating_again(bool throws)
	{
		SeedLoggedIn(createSucceeds: true);
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/characters/new?returnUrl=%2Fwelcome%3Fresume%3Dtrue");
		_upgrade.PlayAsAsync(Arg.Any<AccountAuthService.CharacterSummary>()).Returns(_ => throws
			? Task.FromException<bool>(new System.Net.WebSockets.WebSocketException("offline"))
			: Task.FromResult(false));
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await cut.FindAll("input").First().ChangeAsync("Bob");
		await ClickCreate(cut);
		await Assert.That(cut.Markup).Contains("GuideCharacterConnectionFailed");
		await Assert.That(nav.Uri).Contains("/characters/new");
		await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
		_upgrade.PlayAsAsync(Arg.Any<AccountAuthService.CharacterSummary>()).Returns(Task.FromResult(true));
		await cut.FindAll("button").Single(b => b.TextContent.Trim() == "GuideRetryConnection").ClickAsync();
		await Assert.That(nav.Uri).EndsWith("/welcome?resume=true");
		await _upgrade.Received(2).PlayAsAsync(Arg.Is<AccountAuthService.CharacterSummary>(c => c.Name == "Bob"));
	}

	[TUnit.Core.Test]
	public async Task A_refused_claim_shows_why_and_stays()
	{
		SeedLoggedIn(createSucceeds: false);
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/characters/claim");

		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		await cut.FindAll("input").First().ChangeAsync("Bob");
		await cut.Find("input[type=password]").ChangeAsync("bob-secret");

		await cut.FindAll("button").First(b => b.TextContent.Trim() == "NavClaimCharacter").ClickAsync();

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Invalid character credentials."))
				throw new InvalidOperationException("no refusal shown yet");
		});
		await Assert.That(nav.Uri).EndsWith("/characters/claim");
	}

	/// <summary>A new character signs in through the account; it asks for no password of its own unless ticked.</summary>
	[TUnit.Core.Test]
	public async Task Creating_asks_for_no_character_password_by_default()
	{
		var api = SeedLoggedIn(createSucceeds: true);
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();

		await Assert.That(cut.FindAll("input[type=password]")).IsEmpty();
		await cut.FindAll("input").First().ChangeAsync("Bob");
		await ClickCreate(cut);

		await Assert.That(api.Creates()).IsEqualTo(1);
		await Assert.That(api.LastBody()).Contains("\"password\":null");
		// The account's role comes from its characters, so the first one changes what the tab may do.
		await Assert.That(api.SessionReads()).IsGreaterThan(0);
	}

	/// <summary>A missing name or password is caught before anything is sent, with what to do about it.</summary>
	[TUnit.Core.Test]
	public async Task A_missing_name_or_password_says_what_to_enter()
	{
		var api = SeedLoggedIn(createSucceeds: true);
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		cut.WaitForAssertion(() =>
		{
			if (cut.Find("button.mud-button-filled").HasAttribute("disabled"))
				throw new InvalidOperationException("the roster has not loaded yet");
		});

		await ClickCreate(cut);
		cut.WaitForAssertion(() => cut.Find(".mud-alert:not(#onboarding-notice)"));
		await Assert.That(cut.Find(".mud-alert:not(#onboarding-notice)").TextContent).Contains("NavCharacterNameRequired");

		await cut.FindAll("input").First().ChangeAsync("Bob");
		await cut.Find("input[type=checkbox]").ChangeAsync(true);
		await ClickCreate(cut);
		cut.WaitForAssertion(() =>
		{
			if (!cut.Find(".mud-alert:not(#onboarding-notice)").TextContent.Contains("NavCharacterPasswordRequired"))
				throw new InvalidOperationException("the password refusal has not rendered yet");
		});
		await Assert.That(api.Creates()).IsEqualTo(0);
	}

	/// <summary>The character guide chosen in setup is linked above the name, and only when creating.</summary>
	[TUnit.Core.Test]
	[TUnit.Core.Arguments(false)]
	[TUnit.Core.Arguments(true)]
	public async Task The_character_guide_is_linked_when_creating(bool claim)
	{
		SeedLoggedIn(createSucceeds: true, characterGuide: new GuideLink("Making a character", "/wiki/main/making-a-character"));
		Services.GetRequiredService<NavigationManager>().NavigateTo(claim ? "/characters/claim" : "/characters/new");
		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		cut.WaitForAssertion(() =>
		{
			if (cut.Find("button.mud-button-filled").HasAttribute("disabled"))
				throw new InvalidOperationException("the roster has not loaded yet");
		});
		if (!claim) cut.WaitForAssertion(() => cut.Find(".charcreate-guide a"));

		var links = cut.FindAll(".charcreate-guide a");
		await Assert.That(links.Count).IsEqualTo(claim ? 0 : 1);
		if (!claim)
			await Assert.That(links[0].GetAttribute("href")).IsEqualTo("/wiki/main/making-a-character");
	}
}
