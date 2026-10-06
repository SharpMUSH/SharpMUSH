using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Client.Components.Settings;
using SharpMUSH.Client.Layout;
using SharpMUSH.Tests.BUnit.Components.Characters;

namespace SharpMUSH.Tests.BUnit.Components.Settings;

/// <summary>
/// The Settings section sidebar (README §6.5): "Settings" and who is signed in, an Overview row,
/// Account (Account, Characters) and Preferences (Theme), the current page marked, and the icons
/// alone when collapsed.
/// </summary>
public class SettingsSidebarTests : TrackingBunitContext
{
	public SettingsSidebarTests() => CharactersApiFake.Install(this);

	private BunitNavigationManager Nav => Services.GetRequiredService<BunitNavigationManager>();

	private async Task<IRenderedComponent<SettingsSidebar>> RenderAtAsync(string path, bool collapsed = false)
	{
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this));
		Nav.NavigateTo(path);
		return Render<SettingsSidebar>(p => p.Add(x => x.Collapsed, collapsed));
	}

	[Test]
	public async Task ListsEverySettingsPage_InOrder()
	{
		var cut = await RenderAtAsync("/settings");
		var hrefs = cut.FindAll("a.kit-row").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(hrefs).IsEquivalentTo(["/settings", "/account", "/account#characters", "/account#passkeys", "/settings/theme"]);
		await Assert.That(cut.FindAll(".kit-section-label").Select(l => l.TextContent.Trim()).ToList())
			.IsEquivalentTo(["Account", "Preferences"]);
	}

	[Test]
	[Arguments("/settings", "/settings")]
	[Arguments("/account", "/account")]
	[Arguments("/settings/theme", "/settings/theme")]
	public async Task MarksTheCurrentPage(string path, string current)
	{
		var cut = await RenderAtAsync(path);
		var marked = cut.FindAll("a.kit-row[aria-current='page']").Select(a => a.GetAttribute("href")).ToList();
		await Assert.That(marked).IsEquivalentTo([current])
			.Because("the Characters and Passkeys rows point into the Account page and are never current on their own");
	}

	[Test]
	public async Task Head_NamesTheSignedInAccount()
	{
		var cut = await RenderAtAsync("/settings");
		await Assert.That(cut.Find(".kit-side-title").TextContent).IsEqualTo("Settings");
		await Assert.That(cut.Find(".kit-side-sub").TextContent).Contains("player");
	}

	[Test]
	public async Task Collapsed_KeepsTheIconsOnly()
	{
		var cut = await RenderAtAsync("/settings/theme", collapsed: true);
		await Assert.That(cut.FindAll(".kit-side-head").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll(".kit-section-label").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("a.kit-row.kit-row--collapsed").Count).IsEqualTo(5);
		await Assert.That(cut.Find("a.kit-row[href='/settings/theme']").GetAttribute("aria-label")).IsEqualTo("Theme");
	}

	[Test]
	public async Task Layout_PutsTheSidebarInTheShellSlot_BesideTheBody()
	{
		Services.AddSingleton(await CharactersApiFake.SignedInAsync(this));
		Nav.NavigateTo("/settings");
		var cut = Render<PageSidebarHost>(h => h.AddChildContent<SettingsLayout>(p => p.Add(x => x.Body, b => b.AddMarkupContent(0, "<p id=\"body\">x</p>"))));
		await Assert.That(cut.Find(".test-pagebar .kit-pagebar.settings-shell .settings-side")).IsNotNull();
		await Assert.That(cut.Find(".kit-section-body.settings-shell #body")).IsNotNull();
	}
}
