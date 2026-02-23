using Bunit;
using SharpMUSH.Client.Pages;

namespace SharpMUSH.Tests.Client.Components;

/// <summary>
/// Tests for the Counter component to verify basic Blazor component behavior.
/// </summary>
public class CounterTests : MudBlazorTestContext
{
	[Test]
	public async Task Counter_InitialState_DisplaysZero()
	{
		// Arrange & Act
		var cut = Render<Counter>();

		// Assert
		var statusParagraph = cut.Find("p[role='status']");
		await Assert.That(statusParagraph.TextContent).Contains("Current count: 0");
	}

	[Test]
	public async Task Counter_ClickButton_IncrementsCount()
	{
		// Arrange
		var cut = Render<Counter>();

		// Act
		var button = cut.Find("button");
		button.Click();

		// Assert
		var statusParagraph = cut.Find("p[role='status']");
		await Assert.That(statusParagraph.TextContent).Contains("Current count: 1");
	}

	[Test]
	public async Task Counter_MultipleClicks_IncrementsCorrectly()
	{
		// Arrange
		var cut = Render<Counter>();
		var button = cut.Find("button");

		// Act
		button.Click();
		button.Click();
		button.Click();

		// Assert
		var statusParagraph = cut.Find("p[role='status']");
		await Assert.That(statusParagraph.TextContent).Contains("Current count: 3");
	}

	[Test]
	public async Task Counter_RendersTitle()
	{
		// Arrange & Act
		var cut = Render<Counter>();

		// Assert
		var heading = cut.Find("h1");
		await Assert.That(heading.TextContent).IsEqualTo("Counter");
	}

	[Test]
	public async Task Counter_ButtonHasCorrectText()
	{
		// Arrange & Act
		var cut = Render<Counter>();

		// Assert
		var button = cut.Find("button");
		await Assert.That(button.TextContent).Contains("Click me");
	}
}
