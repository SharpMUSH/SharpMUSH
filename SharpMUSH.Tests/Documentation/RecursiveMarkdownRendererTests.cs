namespace SharpMUSH.Tests.Documentation;

public class RecursiveMarkdownRendererTests
{
	// ANSI escape codes for formatting
	private const string ESC = "\u001b";
	private const string CSI = "\u001b[";
	private const string Bold = "\u001b[1m";
	private const string Faint = "\u001b[2m";
	private const string Underlined = "\u001b[4m";
	private const string Clear = "\u001b[0m";
	private static string Foreground(byte r, byte g, byte b) => $"\u001b[38;2;{r};{g};{b}m";

	[Test]
	public async Task RenderPlainText_ShouldWork()
	{
		// Arrange
		var markdown = "Hello, world!";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Hello, world!");
		await Assert.That(result.ToString()).IsEqualTo("Hello, world!");
	}

	[Test]
	public async Task RenderBoldText_ShouldApplyMarkup()
	{
		// Arrange
		var markdown = "This is **bold** text";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - check exact plain text output
		await Assert.That(result.ToPlainText()).IsEqualTo("This is bold text");

		// Verify ANSI formatting is present (Bold and foreground color)
		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains(Foreground(255, 255, 255))).IsTrue();
		// The word "bold" should be between the formatting codes
		var boldIndex = fullString.IndexOf("bold");
		var ansiIndex = fullString.IndexOf(Bold);
		await Assert.That(ansiIndex).IsLessThan(boldIndex);
	}

	[Test]
	public async Task RenderItalicText_ShouldApplyMarkup()
	{
		// Arrange
		var markdown = "This is *italic* text";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - italic is rendered as bold in this implementation
		await Assert.That(result.ToPlainText()).IsEqualTo("This is italic text");

		// Verify ANSI formatting is present
		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains(Foreground(255, 255, 255))).IsTrue();
		// The word "italic" should be after the formatting codes
		var italicIndex = fullString.IndexOf("italic");
		var ansiIndex = fullString.IndexOf(Bold);
		await Assert.That(ansiIndex).IsLessThan(italicIndex);
	}

	[Test]
	public async Task RenderTable_WithMaxWidth_ShouldConstrainColumns()
	{
		// Arrange
		var markdown = @"| Column1 | Column2 | Column3 |
| --- | --- | --- |
| A | B | C |";

		// Act - set maxWidth to 50 to force column constraint (realistic for 3 columns)
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown, maxWidth: 50);

		// Assert - columns should be constrained to fit within maxWidth
		var plainText = result.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		foreach (var line in lines)
		{
			// Each line should fit within maxWidth
			await Assert.That(line.Length).IsLessThanOrEqualTo(50);
		}

		// Verify content is still present
		await Assert.That(plainText.Contains("Column1")).IsTrue();
		await Assert.That(plainText.Contains("Column2")).IsTrue();
		await Assert.That(plainText.Contains("Column3")).IsTrue();
	}

	[Test]
	public async Task RenderTable_WithAlignment_ShouldUseAlignFunction()
	{
		// Arrange
		var markdown = @"| Left | Center | Right |
| :--- | :---: | ---: |
| L1 | C1 | R1 |";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - verify table contains all content
		var plainText = result.ToPlainText();
		await Assert.That(plainText.Length).IsGreaterThan(0);

		// Verify all data is present
		await Assert.That(plainText.Contains("Left")).IsTrue();
		await Assert.That(plainText.Contains("Center")).IsTrue();
		await Assert.That(plainText.Contains("Right")).IsTrue();
		await Assert.That(plainText.Contains("L1")).IsTrue();
		await Assert.That(plainText.Contains("C1")).IsTrue();
		await Assert.That(plainText.Contains("R1")).IsTrue();

		// Should contain ANSI codes for faint borders
		await Assert.That(result.ToString().Contains(Faint)).IsTrue();
	}

	[Test]
	public async Task RenderList_WithBullets_ShouldHaveFaintFormatting()
	{
		// Arrange
		var markdown = "- Item 1\n- Item 2";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("- Item 1\n- Item 2");
		// Bullets should have faint ANSI formatting
		await Assert.That(result.ToString().Contains(Faint)).IsTrue();
		await Assert.That(result.ToString().Contains("Item 1")).IsTrue();
		await Assert.That(result.ToString().Contains("Item 2")).IsTrue();
	}

	[Test]
	public async Task RenderOrderedList_ShouldHaveFaintNumbers()
	{
		// Arrange
		var markdown = "1. First\n2. Second";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("1. First\n2. Second");
		// Numbers should have faint ANSI formatting
		await Assert.That(result.ToString().Contains(Faint)).IsTrue();
	}

	[Test]
	public async Task RenderHeading1_ShouldHaveUnderlineAndBold()
	{
		// Arrange
		var markdown = "# Heading 1";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 1");
		// H1 should have underline + bold ANSI codes
		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Underlined)).IsTrue();
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains("Heading 1")).IsTrue();
	}

	[Test]
	public async Task RenderHeading2_ShouldHaveUnderlineAndBold()
	{
		// Arrange
		var markdown = "## Heading 2";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 2");
		// H2 should have underline + bold ANSI codes
		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Underlined)).IsTrue();
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains("Heading 2")).IsTrue();
	}

	[Test]
	public async Task RenderHeading3_ShouldHaveUnderline()
	{
		// Arrange
		var markdown = "### Heading 3";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 3");
		// H3 should have underline ANSI code
		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Underlined)).IsTrue();
		await Assert.That(fullString.Contains("Heading 3")).IsTrue();
	}

	[Test]
	public async Task RenderCodeBlock_ShouldPreserveContent()
	{
		// Arrange
		var markdown = "```\ncode line 1\ncode line 2\n```";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - code blocks should be indented by 2 spaces
		await Assert.That(result.ToPlainText()).IsEqualTo("  code line 1\n  code line 2");
		await Assert.That(result.ToString()).IsEqualTo("  code line 1\n  code line 2");
	}

	[Test]
	public async Task RenderInlineCode_ShouldPreserveContent()
	{
		// Arrange
		var markdown = "This is `inline code` here";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("This is inline code here");
		await Assert.That(result.ToString()).IsEqualTo("This is inline code here");
	}

	[Test]
	public async Task RenderQuote_ShouldIndent()
	{
		// Arrange
		var markdown = "> This is a quote";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert
		var plainText = result.ToPlainText();
		await Assert.That(plainText).IsEqualTo("  This is a quote");
	}

	[Test]
	public async Task RenderLink_ShouldShowTextAndUrl()
	{
		// Arrange
		var markdown = "[Link Text](https://example.com)";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - Links use ANSI hyperlink with text as visible content
		// The URL is embedded in ANSI OSC 8 escape codes
		await Assert.That(result.ToPlainText()).IsEqualTo("Link Text");
		// Verify the full string contains the hyperlink escape sequence with URL
		var fullString = result.ToString();
		await Assert.That(fullString).Contains("https://example.com");
		await Assert.That(fullString).Contains("\u001b]8;;");
	}

	[Test]
	public async Task RenderTable_ShouldFitToWidth()
	{
		// Arrange
		var markdown = @"| A | B |
|---|---|
| 1 | 2 |";

		// Act - use default width of 78
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var lines = result.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

		// Assert - table should expand to use available width
		// With 2 columns and borders, total width should be close to 78
		// Each line should be the same length and close to maxWidth
		var firstLineLength = lines[0].Length;
		await Assert.That(firstLineLength).IsGreaterThan(20); // Should be expanded, not minimal
		await Assert.That(firstLineLength).IsLessThanOrEqualTo(78); // Should fit within maxWidth

		// All data rows should have the same length
		foreach (var line in lines.Where(l => !l.Contains("---")))
		{
			await Assert.That(line.Length).IsEqualTo(firstLineLength);
		}
	}

	[Test]
	public async Task RenderTable_DefaultWidthIs78()
	{
		// Arrange
		var markdown = @"| Column 1 | Column 2 | Column 3 |
|---|---|---|
| A | B | C |";

		// Act - use default width (should be 78)
		var resultDefault = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var linesDefault = resultDefault.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

		// Act - explicitly use 78
		var result78 = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown, maxWidth: 78);
		var lines78 = result78.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

		// Assert - both should produce the same output
		await Assert.That(linesDefault[0].Length).IsEqualTo(lines78[0].Length);
		await Assert.That(resultDefault.ToPlainText()).IsEqualTo(result78.ToPlainText());
	}

	[Test]
	public async Task RenderHtml_BoldTag_ShouldStripTags()
	{
		// Arrange
		var markdown = "This is <b>bold</b> text";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - HTML tags should be stripped, leaving just the content
		await Assert.That(result.ToPlainText()).IsEqualTo("This is bold text");
	}

	[Test]
	public async Task RenderHtml_ItalicTag_ShouldStripTags()
	{
		// Arrange
		var markdown = "This is <i>italic</i> text";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - HTML tags should be stripped
		await Assert.That(result.ToPlainText()).IsEqualTo("This is italic text");
	}

	[Test]
	public async Task RenderHtml_UnderlineTag_ShouldStripTags()
	{
		// Arrange
		var markdown = "This is <u>underlined</u> text";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - HTML tags should be stripped
		await Assert.That(result.ToPlainText()).IsEqualTo("This is underlined text");
	}

	[Test]
	public async Task RenderHtml_FontColorTag_ShouldStripTags()
	{
		// Arrange
		var markdown = @"This is <font color=""red"">red</font> text";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - HTML tags should be stripped
		await Assert.That(result.ToPlainText()).IsEqualTo("This is red text");
	}

	[Test]
	public async Task RenderHtml_SpanWithStyle_ShouldStripTags()
	{
		// Arrange
		var markdown = @"This is <span style=""color: #FF0000"">red</span> text";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - HTML tags should be stripped
		await Assert.That(result.ToPlainText()).IsEqualTo("This is red text");
	}

	[Test]
	public async Task RenderHtml_SpanWithBackgroundColor_ShouldStripTags()
	{
		// Arrange
		var markdown = @"This is <span style=""background-color: blue"">highlighted</span> text";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - HTML tags should be stripped
		await Assert.That(result.ToPlainText()).IsEqualTo("This is highlighted text");
	}
}
