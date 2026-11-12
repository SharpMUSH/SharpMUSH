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

/// <summary>One character with the given look, and the appearance it was last sent.</summary>
internal sealed class LookApiHandler(string? themeId, string? accent, bool acting = true) : HttpMessageHandler
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
					new { DbrefNumber = 7, CreationTime = 1L, Name = "Iris", Flags = "PLAYER", IsActing = acting, ThemeId = themeId, Accent = accent, Vision = (string?)null },
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

/// <summary>
/// The player's theme picker: each theme shown as a small page in it, "Try it" painting the portal without saving,
/// and no colour code shown to the player.
/// </summary>
public class SettingsThemePickerTests : TrackingBunitContext
{
	private readonly IThemeService _themes = Substitute.For<IThemeService>();

	private LookApiHandler Seed(string? themeId = null, string? accent = null, bool acting = true)
	{
		this.AddAuthorization().SetAuthorized("headwiz");
		var handler = new LookApiHandler(themeId, accent, acting);
		var client = Track(new HttpClient(handler) { BaseAddress = new Uri("https://localhost:8081/") });
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		_themes.Themes.Returns(BuiltInThemes.All);
		_themes.Defaults.Returns(new PortalThemeDefaults(BuiltInThemes.PhosphorId, BuiltInThemes.DaylightId));

		Services
			.AddMudServices()
			.AddSingleton(factory)
			.AddEchoLocalizer()
			.AddSingleton(_themes)
			.AddSingleton(sp => new AccountAuthService(factory, sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>(),
				NullLogger<AccountAuthService>.Instance, []));

		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.loggedOut").SetResult(null);
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("session-token-1");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("headwiz");
		return handler;
	}

	private static string Option(string themeId) => $".settings-theme-option[data-theme='{themeId}']";

	[Test]
	public async Task EveryPublishedThemeIsShownAsAPageInItsOwnLook()
	{
		Seed();
		var cut = Render<SettingsTheme>();
		cut.WaitForElement(Option("default"));

		var published = BuiltInThemes.All.Where(t => t.Published).ToList();
		foreach (var theme in published)
		{
			var preview = cut.Find($"{Option(theme.Id)} .theme-preview");
			var resolved = ThemeResolver.Resolve(theme);
			await Assert.That(preview.GetAttribute("style")).Contains($"--surface:{resolved.Token(ThemeTokens.Surface)};");
			foreach (var (part, value) in resolved.Parts.Where(p => p.Key != ThemeStyles.Motion))
			{
				await Assert.That(preview.GetAttribute($"data-{part}")).IsEqualTo(value);
			}

			// A page of previews stands still, as under Reduce motion.
			await Assert.That(preview.GetAttribute($"data-{ThemeStyles.Motion}")).IsEqualTo("still");
			await Assert.That(preview.GetAttribute("data-reduce-motion")).IsEqualTo("on");

			await Assert.That(preview.QuerySelector(".theme-preview-title")!.TextContent).IsEqualTo(theme.Name);
			await Assert.That(preview.QuerySelector(".theme-preview-card .theme-marks")).IsNotNull();
		}

		await Assert.That(cut.FindAll(".settings-theme-option").Count).IsEqualTo(published.Count + 1);
		await Assert.That(cut.FindAll(".settings-theme-choice .theme-swatch")).IsEmpty();
	}

	[Test]
	public async Task TryingAThemePaintsThePortalWithoutSaving()
	{
		var handler = Seed(acting: false);
		var cut = Render<SettingsTheme>();
		await cut.WaitForElement($"{Option(BuiltInThemes.DaylightId)} .settings-theme-try").ClickAsync(new());

		_themes.Received(1).Preview(Arg.Is<ResolvedTheme?>(t => t != null && t.ThemeId == BuiltInThemes.DaylightId));
		await Assert.That(handler.Sent).IsNull();
		await Assert.That(cut.Find("#theme-trying").TextContent).Contains("NavThemeTrying(Daylight, Iris)");
		await Assert.That(cut.Find($"{Option(BuiltInThemes.DaylightId)} .settings-theme-try").GetAttribute("aria-pressed")).IsEqualTo("true");

		await cut.Find(".settings-theme-stop").ClickAsync(new());

		_themes.Received(1).Preview(null);
		await Assert.That(cut.FindAll("#theme-trying")).IsEmpty();
		await Assert.That(handler.Sent).IsNull();
	}

	[Test]
	public async Task KeepingATriedThemeSavesIt()
	{
		var handler = Seed(acting: false);
		var cut = Render<SettingsTheme>();
		await cut.WaitForElement($"{Option(BuiltInThemes.DaylightId)} .settings-theme-try").ClickAsync(new());
		await cut.Find(".settings-theme-keep").ClickAsync(new());

		cut.WaitForState(() => handler.Sent is not null);
		await Assert.That(handler.Sent).IsEqualTo(new CharacterAppearance(BuiltInThemes.DaylightId, null, null));
		_themes.Received(1).Preview(null);
	}

	[Test]
	public async Task LeavingThePageStopsTheTrial()
	{
		Seed(acting: false);
		var cut = Render<SettingsTheme>();
		await cut.WaitForElement($"{Option(BuiltInThemes.DaylightId)} .settings-theme-try").ClickAsync(new());

		await DisposeComponentsAsync();

		_themes.Received(1).Preview(null);
	}

	[Test]
	public async Task AnAdjustedAccentIsExplainedWithoutItsColourCode()
	{
		// White cannot be read on Daylight's paper, so it is moved; the player is told so, in words.
		Seed(themeId: BuiltInThemes.DaylightId, accent: "#ffffff");
		var cut = Render<SettingsTheme>();
		var note = cut.WaitForElement("[data-adjusted]");

		await Assert.That(note.TextContent).IsEqualTo("NavThemeAccentAdjusted");
		await Assert.That(cut.Find(".settings-theme-page").TextContent).DoesNotContain("#");
	}

	[Test]
	public async Task NoLanguageNamesTheAdjustedColour()
	{
		foreach (var tag in SharpMUSH.Client.Resources.PortalLocales.Codes)
		{
			using var culture = CultureScope.For(tag);
			await Assert.That(PortalLocalizer.Create()["NavThemeAccentAdjusted"].Value).DoesNotContain("{0}").Because(tag);
		}
	}
}
