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
	public async Task Settings_characters_row_navigates_to_account()
	{
		var cut = Render<Settings>();
		var nav = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

		// Rows are Account / Characters / Theme, in markup order.
		cut.FindAll("button.settings-nav-item")[1].Click();

		await Assert.That(nav.Uri).IsEqualTo($"{nav.BaseUri}account");
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
