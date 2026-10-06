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

/// <summary>Fakes the character-create endpoint (<c>POST api/account/characters</c>).</summary>
file sealed class CharacterCreateApiHandler(bool succeed) : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
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

/// <summary>
/// Coverage for the dedicated create-character page (<c>/characters/new</c>): it renders the form,
/// and a successful create returns the player to <c>/account</c>. Character creation was split out of
/// the account page into its own surface.
/// </summary>
public class CharacterCreatePageTests : TrackingBunitContext, IAsyncDisposable
{
	private ICharacterUpgradeService _upgrade = null!;

	private void SeedLoggedIn(bool createSucceeds)
	{
		this.AddAuthorization().SetAuthorized("headwiz");

		var apiClient = Track(new HttpClient(new CharacterCreateApiHandler(createSucceeds)) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(apiClient);

		Services
			.AddMudServices()
			.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>()
			.AddSingleton(factory)
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
	}

	private static void ClickCreate(IRenderedComponent<SharpMUSH.Client.Pages.CharacterCreate> cut)
		=> cut.FindAll("button").First(b => b.TextContent.Trim() == "NavCreateCharacter").Click();

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
		cut.FindAll("input").First().Change("Bob");

		await cut.InvokeAsync(() => ClickCreate(cut));

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
		cut.FindAll("input").First().Change("imported bob");

		await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "NavClaimCharacter").Click());

		cut.WaitForAssertion(() =>
		{
			if (!nav.Uri.EndsWith("/play"))
				throw new InvalidOperationException("did not navigate to /play yet");
		});
		// The roster takes the name as the game spells it, not as it was typed.
		await _upgrade.Received(1).PlayAsAsync(Arg.Is<AccountAuthService.CharacterSummary>(c => c.Name == "Imported Bob" && c.DbrefNumber == 7));
	}

	[TUnit.Core.Test]
	public async Task A_refused_claim_shows_why_and_stays()
	{
		SeedLoggedIn(createSucceeds: false);
		var nav = Services.GetRequiredService<NavigationManager>();
		nav.NavigateTo("/characters/claim");

		var cut = Render<SharpMUSH.Client.Pages.CharacterCreate>();
		cut.FindAll("input").First().Change("Bob");

		await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "NavClaimCharacter").Click());

		cut.WaitForAssertion(() =>
		{
			if (!cut.Markup.Contains("Invalid character credentials."))
				throw new InvalidOperationException("no refusal shown yet");
		});
		await Assert.That(nav.Uri).EndsWith("/characters/claim");
	}
}
