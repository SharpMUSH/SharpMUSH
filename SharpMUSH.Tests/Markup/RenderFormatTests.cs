using System.Drawing;
using A = MarkupString.MarkupStringModule;
using H = MarkupString.MarkupImplementation.HtmlMarkup;
using M = MarkupString.MarkupImplementation.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Unit tests for the format-aware Render method on MarkupString.
/// </summary>
public class RenderFormatTests
{
	[Test]
	public async Task Render_AnsiFormat_ReturnsAnsiOutput()
	{
		// Arrange
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.Red));
		var markupString = A.markupSingle(ansiMarkup, "Red Text");

		// Act
		var ansiResult = markupString.Render("ansi");
		var toStringResult = markupString.ToString();

		// Assert - "ansi" format should match ToString() behavior
		await Assert.That(ansiResult).IsEqualTo(toStringResult);
	}

	[Test]
	public async Task Render_HtmlFormat_AnsiColorMarkup_ReturnsSpanWithStyle()
	{
		// Arrange
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var markupString = A.markupSingle(ansiMarkup, "Red Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("<span style=\"");
		await Assert.That(result).Contains("color: #ff0000");
		await Assert.That(result).Contains("Red Text");
		await Assert.That(result).Contains("</span>");
		// Should NOT contain ANSI escape codes
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_HtmlFormat_BoldMarkup_ReturnsSpanWithFontWeight()
	{
		// Arrange
		var ansiMarkup = M.Create(bold: true);
		var markupString = A.markupSingle(ansiMarkup, "Bold Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("font-weight: bold");
		await Assert.That(result).Contains("Bold Text");
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_HtmlFormat_ItalicMarkup_ReturnsSpanWithFontStyle()
	{
		// Arrange
		var ansiMarkup = M.Create(italic: true);
		var markupString = A.markupSingle(ansiMarkup, "Italic Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("font-style: italic");
		await Assert.That(result).Contains("Italic Text");
	}

	[Test]
	public async Task Render_HtmlFormat_UnderlinedMarkup_ReturnsSpanWithTextDecoration()
	{
		// Arrange
		var ansiMarkup = M.Create(underlined: true);
		var markupString = A.markupSingle(ansiMarkup, "Underlined Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("text-decoration: underline");
		await Assert.That(result).Contains("Underlined Text");
	}

	[Test]
	public async Task Render_HtmlFormat_BackgroundColor_ReturnsSpanWithBackgroundStyle()
	{
		// Arrange
		var ansiMarkup = M.Create(background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 128, 0)));
		var markupString = A.markupSingle(ansiMarkup, "Green BG");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("background-color: #008000");
		await Assert.That(result).Contains("Green BG");
	}

	[Test]
	public async Task Render_HtmlFormat_HtmlMarkup_ReturnsHtmlTags()
	{
		// Arrange
		var htmlMarkup = H.Create("b");
		var markupString = A.markupSingle(htmlMarkup, "Bold HTML");

		// Act
		var result = markupString.Render("html");

		// Assert - HtmlMarkup renders the same regardless of format
		await Assert.That(result).IsEqualTo("<b>Bold HTML</b>");
	}

	[Test]
	public async Task Render_HtmlFormat_PlainText_ReturnsPlainText()
	{
		// Arrange
		var markupString = A.single("Plain Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).IsEqualTo("Plain Text");
	}

	[Test]
	public async Task Render_HtmlFormat_MultipleStyles_CombinesIntoOneSpan()
	{
		// Arrange
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			bold: true,
			italic: true);
		var markupString = A.markupSingle(ansiMarkup, "Styled Text");

		// Act
		var result = markupString.Render("html");

		// Assert - all styles should be in a single span
		await Assert.That(result).Contains("color: #ff0000");
		await Assert.That(result).Contains("font-weight: bold");
		await Assert.That(result).Contains("font-style: italic");
		await Assert.That(result).Contains("Styled Text");
	}

	[Test]
	public async Task Render_HtmlFormat_NoMarkup_ReturnsPlainText()
	{
		// Arrange
		var ansiMarkup = M.Create();  // no styling
		var markupString = A.markupSingle(ansiMarkup, "Plain ANSI");

		// Act
		var result = markupString.Render("html");

		// Assert - no span when there's nothing to style
		await Assert.That(result).IsEqualTo("Plain ANSI");
		await Assert.That(result).DoesNotContain("<span");
	}

	[Test]
	public async Task Render_HtmlFormat_StrikeThrough_ReturnsSpanWithLineThrough()
	{
		// Arrange
		var ansiMarkup = M.Create(strikeThrough: true);
		var markupString = A.markupSingle(ansiMarkup, "Strike Text");

		// Act
		var result = markupString.Render("html");

		// Assert
		await Assert.That(result).Contains("text-decoration: line-through");
	}

	[Test]
	public async Task Render_HtmlFormat_ConcatenatedMarkup_RendersEachSegment()
	{
		// Arrange
		var redMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)));
		var blueMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)));

		var redText = A.markupSingle(redMarkup, "Red");
		var blueText = A.markupSingle(blueMarkup, "Blue");
		var combined = A.concat(redText, blueText);

		// Act
		var result = combined.Render("html");

		// Assert
		await Assert.That(result).Contains("color: #ff0000");
		await Assert.That(result).Contains("color: #0000ff");
		await Assert.That(result).Contains("Red");
		await Assert.That(result).Contains("Blue");
		await Assert.That(result).DoesNotContain("\u001b[");
	}

	[Test]
	public async Task Render_ModuleLevelFunction_WorksCorrectly()
	{
		// Arrange
		var ansiMarkup = M.Create(foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 165, 0)));
		var markupString = A.markupSingle(ansiMarkup, "Orange");

		// Act - use module-level render function
		var result = A.render("html", markupString);

		// Assert
		await Assert.That(result).Contains("color: #ffa500");
		await Assert.That(result).Contains("Orange");
	}

	[Test]
	public async Task Render_InvertedMarkup_SwapsForegroundAndBackground()
	{
		// Arrange - fg=red, bg=blue, inverted=true → swap: fg=blue, bg=red
		var ansiMarkup = M.Create(
			foreground: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(255, 0, 0)),
			background: ANSILibrary.ANSI.AnsiColor.NewRGB(Color.FromArgb(0, 0, 255)),
			inverted: true);
		var markupString = A.markupSingle(ansiMarkup, "Inverted");

		// Act
		var result = markupString.Render("html");

		// Assert - original fg (red) becomes background; original bg (blue) becomes foreground
		await Assert.That(result).Contains("background-color: #ff0000");
		await Assert.That(result).Contains("color: #0000ff");
	}
}
