using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>Three characters: Iris acting, then Basil and Clove.</summary>
internal sealed class ThreeCharactersHandler : HttpMessageHandler
{
	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (request.Method == HttpMethod.Get && path == "api/account/characters")
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new[]
				{
					new { DbrefNumber = 9, CreationTime = 1L, Name = "Clove", Flags = "PLAYER", IsActing = false },
					new { DbrefNumber = 7, CreationTime = 1L, Name = "Iris", Flags = "PLAYER", IsActing = true },
					new { DbrefNumber = 8, CreationTime = 1L, Name = "Basil", Flags = "PLAYER", IsActing = false },
				}),
			});
		}

		return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
	}
}

/// <summary>
/// One character's theme settings are open at a time, so the page draws one set of theme previews rather than one per
/// character.
/// </summary>
public class SettingsThemeAccordionTests : TrackingBunitContext
{
	private void Seed()
	{
		this.AddAuthorization().SetAuthorized("headwiz");
		var client = Track(new HttpClient(new ThreeCharactersHandler()) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		var themes = Substitute.For<IThemeService>();
		themes.Themes.Returns(BuiltInThemes.All);
		themes.Defaults.Returns(new PortalThemeDefaults(BuiltInThemes.PhosphorId, BuiltInThemes.DaylightId));

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddEchoLocalizer()
			.AddSingleton(themes)
			.AddSingleton(sp => new AccountAuthService(factory, sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, []));

		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
	}

	private static string Head(int dbref) => $"[data-character='{dbref}'] .settings-theme-head";

	private static string Body(int dbref) => $"[data-character='{dbref}'] .settings-theme-body";

	[Test]
	public async Task TheActingCharacterOpensAloneAndOpeningAnotherClosesIt()
	{
		Seed();
		var cut = Render<SettingsTheme>();
		cut.WaitForElement(Body(7));

		await Assert.That(cut.FindAll(".settings-theme-head").Count).IsEqualTo(3);
		await Assert.That(cut.FindAll(".settings-theme-body").Count).IsEqualTo(1);
		await Assert.That(cut.Find(Head(7)).GetAttribute("aria-expanded")).IsEqualTo("true");
		await Assert.That(cut.Find(Head(8)).GetAttribute("aria-expanded")).IsEqualTo("false");
		var published = BuiltInThemes.All.Count(t => t.Published);
		await Assert.That(cut.FindAll(".theme-preview").Count).IsEqualTo(published + 1);

		await cut.Find(Head(8)).ClickAsync(new());

		await Assert.That(cut.FindAll(".settings-theme-body").Count).IsEqualTo(1);
		await Assert.That(cut.FindAll(Body(8))).HasSingleItem();
		await Assert.That(cut.Find(Head(7)).GetAttribute("aria-expanded")).IsEqualTo("false");
		await Assert.That(cut.Find(Head(7)).HasAttribute("aria-controls")).IsFalse().Because("a closed character has no panel to point at");
		await Assert.That(cut.Find(Head(8)).GetAttribute("aria-controls")).IsEqualTo("theme-body-8");

		await cut.Find(Head(8)).ClickAsync(new());

		await Assert.That(cut.FindAll(".settings-theme-body")).IsEmpty().Because("opening the open one closes it");
	}

	[Test]
	public async Task ALinkToACharacterOpensIt()
	{
		Seed();
		Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/theme#theme-9");
		var cut = Render<SettingsTheme>();
		cut.WaitForElement(Body(9));

		await Assert.That(cut.FindAll(".settings-theme-body").Count).IsEqualTo(1);
	}

	[Test]
	public async Task ALinkToAnotherAccountsCharacterOpensTheActingOne()
	{
		Seed();
		Services.GetRequiredService<NavigationManager>().NavigateTo("/settings/theme#theme-99");
		var cut = Render<SettingsTheme>();
		cut.WaitForElement(Body(7));

		await Assert.That(cut.FindAll(".settings-theme-body").Count).IsEqualTo(1);
	}

	[Test]
	public async Task EveryCharactersSwatchStandsStill()
	{
		Seed();
		var cut = Render<SettingsTheme>();
		cut.WaitForElement(Body(7));

		var swatches = cut.FindAll(".settings-theme-head .theme-swatch");
		await Assert.That(swatches.Count).IsEqualTo(3);
		foreach (var swatch in swatches)
		{
			await Assert.That(swatch.GetAttribute("data-motion")).IsEqualTo("still");
			await Assert.That(swatch.GetAttribute("data-reduce-motion")).IsEqualTo("on");
		}
	}

	[Test]
	public async Task OnlyOtherCharactersOfferTryIt()
	{
		// The portal wears the acting character's look, so choosing a theme for it already shows it.
		Seed();
		var cut = Render<SettingsTheme>();
		cut.WaitForElement(Body(7));
		await Assert.That(cut.FindAll($"{Body(7)} .settings-theme-try")).IsEmpty();

		await cut.Find(Head(8)).ClickAsync(new());

		await Assert.That(cut.FindAll($"{Body(8)} .settings-theme-try").Count).IsEqualTo(BuiltInThemes.All.Count(t => t.Published) + 1);
	}
}
