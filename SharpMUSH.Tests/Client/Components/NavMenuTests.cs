using Bunit;
using TUnit.Core;
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
		var cut = RenderComponent<NavMenu>();

		// Assert - Verify all expected navigation links are present
		var homeLink = cut.Find("a[href='/']");
		var counterLink = cut.Find("a[href='/counter']");
		var weatherLink = cut.Find("a[href='/weather']");
		var aboutLink = cut.Find("a[href='/about']");

		await Assert.That(homeLink).IsNotNull();
		await Assert.That(counterLink).IsNotNull();
		await Assert.That(weatherLink).IsNotNull();
		await Assert.That(aboutLink).IsNotNull();
	}

	[Test]
	public async Task NavMenu_HomeLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = RenderComponent<NavMenu>();

		// Assert
		var homeLink = cut.Find("a[href='/']");
		await Assert.That(homeLink.TextContent).Contains("Home");
	}

	[Test]
	public async Task NavMenu_CounterLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = RenderComponent<NavMenu>();

		// Assert
		var counterLink = cut.Find("a[href='/counter']");
		await Assert.That(counterLink.TextContent).Contains("Counter");
	}

	[Test]
	public async Task NavMenu_WeatherLinkText_IsCorrect()
	{
		// Arrange & Act
		var cut = RenderComponent<NavMenu>();

		// Assert
		var weatherLink = cut.Find("a[href='/weather']");
		await Assert.That(weatherLink.TextContent).Contains("Weather");
	}

	[Test]
	public async Task NavMenu_SettingsGroupExists()
	{
		// Arrange & Act
		var cut = RenderComponent<NavMenu>();

		// Assert - Verify Settings group with Config link exists
		var configLink = cut.Find("a[href='/admin/config']");
		await Assert.That(configLink).IsNotNull();
		await Assert.That(configLink.TextContent).Contains("Config");
	}

	[Test]
	public async Task NavMenu_SecurityLinkExists()
	{
		// Arrange & Act
		var cut = RenderComponent<NavMenu>();

		// Assert
		var securityLink = cut.Find("a[href='/security']");
		await Assert.That(securityLink).IsNotNull();
		await Assert.That(securityLink.TextContent).Contains("Security");
	}

	[Test]
	public async Task NavMenu_ApplicationTitleExists()
	{
		// Arrange & Act
		var cut = RenderComponent<NavMenu>();

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).Contains("My Application");
	}

	[Test]
	public async Task NavMenu_SecondaryTextExists()
	{
		// Arrange & Act
		var cut = RenderComponent<NavMenu>();

		// Assert
		var markup = cut.Markup;
		await Assert.That(markup).Contains("Secondary Text");
	}
}
