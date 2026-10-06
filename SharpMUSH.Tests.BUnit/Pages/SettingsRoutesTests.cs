using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using SharpMUSH.Client.Pages;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// /settings advertised "Manage characters linked to your account" and navigated to
/// /settings/characters — a "Coming soon" placeholder whose only action was a link back to
/// /account, where linking, unlinking and creating characters has always actually worked. The stub
/// is gone; the row goes straight to /account and the orphaned route redirects there so an existing
/// bookmark does not land on the not-found page.
/// </summary>
public class SettingsRoutesTests : BunitContext
{
	public SettingsRoutesTests()
	{
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		JSInterop.Mode = JSRuntimeMode.Loose;
	}

	[Test]
	public async Task Settings_characters_row_links_to_the_account_pages_characters_card()
	{
		var cut = Render<Settings>();

		// Cards are Account / Characters / Passkeys / Theme, in markup order, and are real links.
		var hrefs = cut.FindAll("a.kit-link-card").Select(a => a.GetAttribute("href")).ToList();

		await Assert.That(hrefs).IsEquivalentTo(["/account", "/account#characters", "/account#passkeys", "/settings/theme"]);
	}

	[Test]
	public async Task Settings_uses_the_plain_header_and_the_settings_section()
	{
		var cut = Render<Settings>();

		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("Settings");
		foreach (var page in new[] { typeof(Settings), typeof(SettingsTheme), typeof(Account) })
		{
			var layout = page.GetCustomAttributes(typeof(LayoutAttribute), false).Cast<LayoutAttribute>().Single();
			await Assert.That(layout.LayoutType).IsEqualTo(typeof(SharpMUSH.Client.Layout.SettingsLayout)).Because(page.Name);
		}
	}

	[Test]
	public async Task Theme_is_a_card_under_the_plain_header()
	{
		var cut = Render<SettingsTheme>();

		await Assert.That(cut.Find(".kit-page-head .kit-page-title").TextContent).IsEqualTo("NavTheme");
		await Assert.That(cut.Find(".kit-card .kit-card-title").TextContent).IsEqualTo("ComingSoon");
	}

	[Test]
	public async Task Settings_offers_no_route_to_the_deleted_stub()
	{
		var cut = Render<Settings>();

		await Assert.That(cut.Markup).DoesNotContain("/settings/characters");
	}

	[Test]
	public async Task The_orphaned_settings_characters_route_redirects_to_account()
	{
		Render<SettingsCharactersRedirect>();
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

		await Assert.That(nav.Uri).IsEqualTo($"{nav.BaseUri}account");
	}
}
