using Bunit;
using SharpMUSH.Client.Layout;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the NavMenu component to verify navigation menu behavior.
/// </summary>
public class NavMenuTests : MudBlazorTestContext
{
	[Test]
	public async Task NavMenu_RendersAllNavigationLinks()
	{
		// Arrange & Act
		var cut = Render<NavMenu>();

		// Assert - Verify all expected navigation links are present
		var homeLink = cut.Find("a[href='/']");
		var softcodeLink = cut.Find("a[href='/softcode']");
		var accountLink = cut.Find("a[href='/account']");

		await Assert.That(homeLink).IsNotNull();
		await Assert.That(softcodeLink).IsNotNull();
		await Assert.That(accountLink).IsNotNull();
	}

	[Test]
	public async Task NavMenu_HomeLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = Render<NavMenu>();

		// Assert
		var homeLink = cut.Find("a[href='/']");
		await Assert.That(homeLink.TextContent).Contains("Home");
	}

	[Test]
	public async Task NavMenu_SoftcodeLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = Render<NavMenu>();

		// Assert
		var softcodeLink = cut.Find("a[href='/softcode']");
		await Assert.That(softcodeLink.TextContent).Contains("Softcode");
	}

	[Test]
	public async Task NavMenu_AccountLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = Render<NavMenu>();

		// Assert
		var accountLink = cut.Find("a[href='/account']");
		await Assert.That(accountLink.TextContent).Contains("My Account");
	}

	[Test]
	public async Task NavMenu_SettingsGroupExists()
	{
		// Arrange & Act
		var cut = Render<NavMenu>();

		// Assert - Verify Settings group with Config link exists
		var configLink = cut.Find("a[href='/admin/config']");
		await Assert.That(configLink).IsNotNull();
		await Assert.That(configLink.TextContent).Contains("Config");
	}

	[Test]
	public async Task NavMenu_SecurityLinkExists()
	{
		// Arrange & Act
		var cut = Render<NavMenu>();

		// Assert
		var securityLink = cut.Find("a[href='/security']");
		await Assert.That(securityLink).IsNotNull();
		await Assert.That(securityLink.TextContent).Contains("Security");
	}
}
