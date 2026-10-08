using System.Net;
using System.Net.Http.Json;
using Bunit;
using Bunit.TestDoubles;
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

/// <summary>The roster of one character, and the appearance it was last sent, echoed back as stored.</summary>
internal sealed class AppearanceApiHandler(string? vision) : HttpMessageHandler
{
	public CharacterAppearance? Sent { get; private set; }

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		if (request.Method == HttpMethod.Get && path == "api/account/characters")
		{
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = JsonContent.Create(new[]
				{
					new { DbrefNumber = 7, CreationTime = 1L, Name = "Iris", Flags = "PLAYER", IsActing = true, ThemeId = (string?)null, Accent = (string?)null, Vision = vision },
				}),
			};
		}

		if (request.Method == HttpMethod.Put && path == "api/account/characters/7/appearance")
		{
			Sent = await request.Content!.ReadFromJsonAsync<CharacterAppearance>(cancellationToken);
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Sent) };
		}

		return new HttpResponseMessage(HttpStatusCode.NotFound);
	}
}

/// <summary>The colour vision a player names on the theme settings page.</summary>
public class SettingsThemeVisionTests : TrackingBunitContext
{
	private AppearanceApiHandler Seed(string? vision)
	{
		this.AddAuthorization().SetAuthorized("headwiz");
		var handler = new AppearanceApiHandler(vision);
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
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
		return handler;
	}

	[Test]
	public async Task EveryVisionIsOfferedWithWhatItMeans()
	{
		Seed(vision: null);
		var cut = Render<SettingsTheme>();
		cut.WaitForElement("[data-vision='typical']");

		foreach (var vision in ThemeVision.All)
		{
			var choice = cut.Find($"[data-vision='{vision}']");
			await Assert.That(choice.GetAttribute("role")).IsEqualTo("radio");
			await Assert.That(choice.GetAttribute("aria-checked")).IsEqualTo(vision == ThemeVision.Typical ? "true" : "false");
			await Assert.That(choice.TextContent).Contains("What");
		}

		await Assert.That(cut.Markup).Contains("NavThemeVisionNote");
	}

	[Test]
	public async Task ChoosingAVisionKeepsTheThemeAndAccent()
	{
		var handler = Seed(vision: null);
		var cut = Render<SettingsTheme>();
		await cut.WaitForElement("[data-vision='tritan']").ClickAsync(new());

		cut.WaitForState(() => cut.Find("[data-vision='tritan']").GetAttribute("aria-checked") == "true");
		await Assert.That(handler.Sent).IsEqualTo(new CharacterAppearance(null, null, ThemeVision.Tritan));
		await Assert.That(cut.Markup).Contains("NavThemeVisionFollows");
	}

	[Test]
	public async Task WithAVisionTheDefaultIsThatVisionsPair()
	{
		Seed(vision: ThemeVision.Deutan);
		var cut = Render<SettingsTheme>();

		cut.WaitForState(() => cut.FindAll("[data-theme='default']").Any(e => e.TextContent.Contains("NavThemeVisionDefault(Deutan Dark / Deutan Light)", StringComparison.Ordinal)));
		await Assert.That(cut.Find("[data-vision='deutan']").GetAttribute("aria-checked")).IsEqualTo("true");
	}
}
