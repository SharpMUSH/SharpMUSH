using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Layout;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.BUnit.Components;
using SharpMUSH.Tests.BUnit.Resources;
using CharacterSummary = SharpMUSH.Client.Services.AccountAuthService.CharacterSummary;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// A player turns the theme's picture tone off and on: quickly from the account menu for the acting character, or on
/// the Theme page for each character. Either way the rest of the look is kept.
/// </summary>
public class ImageFiltersToggleTests : TrackingBunitContext
{
	private static readonly CharacterSummary Acting = new(7, 1, "Iris", "PLAYER", IsActing: true, ThemeId: "directive", Accent: "#ffb454");

	[Test]
	public async Task TheAccountMenuSwitchShowsAndFlipsTheActingCharactersFilters()
	{
		Services.AddMudServices().AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
		var asked = new List<bool>();
		var cut = Render<MudHarness>(p => p.AddChildContent<AccountPanel>(panel => panel
			.Add(x => x.IsOpen, true)
			.Add(x => x.ActiveCharacter, Acting with { ImageFilters = false })
			.Add(x => x.OnSetImageFilters, EventCallback.Factory.Create<bool>(this, on => asked.Add(on)))));

		var toggle = cut.Find(".account-panel-filters");
		await Assert.That(toggle.GetAttribute("role")).IsEqualTo("switch");
		await Assert.That(toggle.GetAttribute("aria-checked")).IsEqualTo("false");

		await toggle.ClickAsync(new());

		await Assert.That(asked).IsEquivalentTo([true]);
		await Assert.That(cut.FindAll(".account-panel-filters")).HasSingleItem().Because("the panel stays open for another toggle");
	}

	[Test]
	public async Task TheAccountMenuHasNoSwitchWithoutAnActingCharacter()
	{
		Services.AddMudServices().AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
		var cut = Render<MudHarness>(p => p.AddChildContent<AccountPanel>(panel => panel.Add(x => x.IsOpen, true)));

		await Assert.That(cut.FindAll(".account-panel-filters")).IsEmpty();
	}

	[Test]
	public async Task TheThemePageTurnsFiltersOffAndKeepsThemAcrossOtherChoices()
	{
		var handler = SeedSettings();
		var cut = Render<SettingsTheme>();
		var on = cut.WaitForElement("[data-image-filters='on']");
		await Assert.That(on.GetAttribute("aria-checked")).IsEqualTo("true");
		await Assert.That(cut.Find("[data-image-filters='off']").GetAttribute("aria-checked")).IsEqualTo("false");

		await cut.Find("[data-image-filters='off']").ClickAsync(new());
		cut.WaitForState(() => cut.Find("[data-image-filters='off']").GetAttribute("aria-checked") == "true");
		await Assert.That(handler.Sent).IsEqualTo(new CharacterAppearance(null, null) { ImageFilters = false });

		await cut.Find("[data-vision='tritan']").ClickAsync(new());
		cut.WaitForState(() => cut.Find("[data-vision='tritan']").GetAttribute("aria-checked") == "true");
		await Assert.That(handler.Sent).IsEqualTo(new CharacterAppearance(null, null, ThemeVision.Tritan) { ImageFilters = false })
			.Because("choosing something else keeps the filters as the player left them");
	}

	[Test]
	public async Task TheAccountMenuSwitchFlipsReducedMotion()
	{
		Services.AddMudServices().AddEchoLocalizer();
		JSInterop.Mode = JSRuntimeMode.Loose;
		var asked = new List<bool>();
		var cut = Render<MudHarness>(p => p.AddChildContent<AccountPanel>(panel => panel
			.Add(x => x.IsOpen, true)
			.Add(x => x.ActiveCharacter, Acting)
			.Add(x => x.OnSetReduceMotion, EventCallback.Factory.Create<bool>(this, on => asked.Add(on)))));

		var toggle = cut.Find(".account-panel-motion");
		await Assert.That(toggle.GetAttribute("role")).IsEqualTo("switch");
		await Assert.That(toggle.GetAttribute("aria-checked")).IsEqualTo("false");

		await toggle.ClickAsync(new());

		await Assert.That(asked).IsEquivalentTo([true]);
	}

	[Test]
	public async Task TheThemePageTurnsReducedMotionOnAndKeepsTheFilters()
	{
		var handler = SeedSettings();
		var cut = Render<SettingsTheme>();
		var off = cut.WaitForElement("button[data-reduce-motion='off']");
		await Assert.That(off.GetAttribute("aria-checked")).IsEqualTo("true");

		await cut.Find("[data-image-filters='off']").ClickAsync(new());
		cut.WaitForState(() => cut.Find("[data-image-filters='off']").GetAttribute("aria-checked") == "true");
		await cut.Find("button[data-reduce-motion='on']").ClickAsync(new());
		cut.WaitForState(() => cut.Find("button[data-reduce-motion='on']").GetAttribute("aria-checked") == "true");
		await Assert.That(handler.Sent).IsEqualTo(new CharacterAppearance(null, null) { ImageFilters = false, ReduceMotion = true })
			.Because("turning motion down keeps the filters as the player left them");
	}

	private AppearanceApiHandler SeedSettings()
	{
		this.AddAuthorization().SetAuthorized("headwiz");
		var handler = new AppearanceApiHandler(vision: null);
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
}
