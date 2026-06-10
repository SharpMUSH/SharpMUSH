using Bunit;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the NavMenu component to verify navigation menu behavior.
/// NavMenu defaults to collapsed (icon-only) mode; expanded mode is opt-in
/// via <c>IsCollapsed="false"</c>.
/// </summary>
public class NavMenuTests : MudBlazorTestContext
{
	private IRenderedComponent<NavMenu> RenderExpanded() =>
		Render<NavMenu>(parameters => parameters.Add(p => p.IsCollapsed, false));

	// ── Default (collapsed) mode ─────────────────────────────────────────────

	[Test]
	public async Task NavMenu_Default_IsCollapsed_RendersIconOnlyLinks()
	{
		// Arrange & Act — no parameters: collapsed by default
		var cut = Render<NavMenu>();

		// Assert - Core links are present but render without text labels
		var homeLink = cut.Find("a[href='/']");
		var softcodeLink = cut.Find("a[href='/softcode']");
		var accountLink = cut.Find("a[href='/account']");

		await Assert.That(homeLink.TextContent.Trim()).IsEmpty();
		await Assert.That(softcodeLink.TextContent.Trim()).IsEmpty();
		await Assert.That(accountLink.TextContent.Trim()).IsEmpty();
	}

	[Test]
	public async Task NavMenu_Default_IsCollapsed_ShowsConfigShortcutWithoutSettingsGroup()
	{
		// Arrange & Act
		var cut = Render<NavMenu>();

		// Assert - Collapsed mode replaces the Settings group with an icon shortcut
		var configLink = cut.Find("a[href='/admin/config']");
		await Assert.That(configLink.TextContent.Trim()).IsEmpty();

		// Group-only links (security, suggestions) are not rendered when collapsed
		await Assert.That(cut.FindAll("a[href='/security']").Count).IsEqualTo(0);
		await Assert.That(cut.FindAll("a[href='/admin/suggestions']").Count).IsEqualTo(0);
	}

	[Test]
	public async Task NavMenu_Default_IsCollapsed_HidesAppTitle()
	{
		// Arrange & Act
		var cut = Render<NavMenu>();

		// Assert - The header block (AppTitle / AdminPanel) only renders expanded
		await Assert.That(cut.FindAll("h6").Count).IsEqualTo(0);
	}

	// ── Expanded mode ────────────────────────────────────────────────────────

	[Test]
	public async Task NavMenu_Expanded_RendersAllNavigationLinks()
	{
		// Arrange & Act
		var cut = RenderExpanded();

		// Assert - Verify all expected navigation links are present
		var homeLink = cut.Find("a[href='/']");
		var softcodeLink = cut.Find("a[href='/softcode']");
		var accountLink = cut.Find("a[href='/account']");

		await Assert.That(homeLink).IsNotNull();
		await Assert.That(softcodeLink).IsNotNull();
		await Assert.That(accountLink).IsNotNull();
	}

	[Test]
	public async Task NavMenu_Expanded_HomeLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = RenderExpanded();

		// Assert
		var homeLink = cut.Find("a[href='/']");
		await Assert.That(homeLink.TextContent).Contains("Home");
	}

	[Test]
	public async Task NavMenu_Expanded_SoftcodeLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = RenderExpanded();

		// Assert
		var softcodeLink = cut.Find("a[href='/softcode']");
		await Assert.That(softcodeLink.TextContent).Contains("Softcode");
	}

	[Test]
	public async Task NavMenu_Expanded_AccountLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = RenderExpanded();

		// Assert
		var accountLink = cut.Find("a[href='/account']");
		await Assert.That(accountLink.TextContent).Contains("My Account");
	}

	[Test]
	public async Task NavMenu_Expanded_SettingsGroupExists()
	{
		// Arrange & Act
		var cut = RenderExpanded();

		// Assert - Verify Settings group with Config link exists
		var configLink = cut.Find("a[href='/admin/config']");
		await Assert.That(configLink).IsNotNull();
		await Assert.That(configLink.TextContent).Contains("Config");
	}

	[Test]
	public async Task NavMenu_Expanded_SecurityLinkExists()
	{
		// Arrange & Act
		var cut = RenderExpanded();

		// Assert
		var securityLink = cut.Find("a[href='/security']");
		await Assert.That(securityLink).IsNotNull();
		await Assert.That(securityLink.TextContent).Contains("Security");
	}
}
