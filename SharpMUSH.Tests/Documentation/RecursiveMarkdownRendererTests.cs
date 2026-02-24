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
	public async Task RenderCodeBlock_WithJsonLanguage_ShouldApplyAnsiColours()
	{
		// Arrange - JSON fenced block with a key and a number
		var markdown = "```json\n{\"hello\": 42}\n```";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var ansi = result.ToString();

		// Assert - plain text must be preserved
		await Assert.That(result.ToPlainText().Trim()).Contains("hello");
		await Assert.That(result.ToPlainText().Trim()).Contains("42");

		// ANSI codes must be present (at least the key "hello" and number 42 get coloured)
		await Assert.That(ansi.Contains(ESC)).IsTrue();

		// The opening brace must appear without an immediately preceding ANSI colour code.
		// Previously SplitLeadingStructural tried to strip it; now the correct Scope.Index/Length
		// walk ensures text[0..scope.Index] (i.e. "{") is emitted as plain before the coloured key.
		await Assert.That(ansi).Contains("{");
		// The brace must NOT be inside an ANSI colour sequence — it should appear either right
		// after the 2-space indent or right after a reset code, never inside a colour span.
		await Assert.That(ansi).DoesNotContain(Foreground(0xFF, 0x87, 0x00) + "{");
	}

	[Test]
	public async Task RenderCodeBlock_WithPythonLanguage_ShouldApplyAnsiColours()
	{
		// Arrange - Python fenced block with a keyword
		var markdown = "```python\ndef hello():\n    return 42\n```";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - plain text preserved
		await Assert.That(result.ToPlainText()).Contains("def");
		await Assert.That(result.ToPlainText()).Contains("hello");

		// Should contain ANSI codes (keyword highlighting at minimum)
		await Assert.That(result.ToString().Contains(ESC)).IsTrue();
	}

	[Test]
	public async Task RenderCodeBlock_WithUnknownLanguage_ShouldFallBackToPlainText()
	{
		// Arrange - unrecognised language tag
		var markdown = "```xyz_unknown_language\nsome code here\n```";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - plain text preserved and no ANSI colouring
		await Assert.That(result.ToPlainText().Trim()).Contains("some code here");
		await Assert.That(result.ToString().Contains(ESC)).IsFalse();
	}

	[Test]
	public async Task RenderCodeBlock_WithNoLanguageTag_ShouldFallBackToPlainText()
	{
		// Arrange - indented code block (no language tag)
		var markdown = "    plain indented code";

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// Assert - plain text preserved and no ANSI colouring
		await Assert.That(result.ToPlainText().Trim()).Contains("plain indented code");
		await Assert.That(result.ToString().Contains(ESC)).IsFalse();
	}
}

/// <summary>
/// Tests for <c>sharp</c> fenced code block syntax highlighting using a real MUSH parser.
/// These tests require the full server DI stack to produce a <see cref="IMUSHCodeParser"/>.
/// </summary>
[NotInParallel]
public class RecursiveMarkdownRendererWithParserTests
{
	private const string ESC = "\u001b";
	private static string Foreground(byte r, byte g, byte b) => $"\u001b[38;2;{r};{g};{b}m";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task RenderSharpCodeBlock_WithRealParser_ShouldApplyFunctionColour()
	{
		// Arrange - a sharp block with a built-in function call
		var markdown = "```sharp\nname(%#)\n```";
		var parser = WebAppFactoryArg.FunctionParser;

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown(markdown, 78, parser);
		var ansi = result.ToString();

		// Assert - plain text preserved
		await Assert.That(result.ToPlainText()).Contains("name");
		await Assert.That(result.ToPlainText()).Contains("%#");

		// ANSI codes must be present (function + substitution are both coloured)
		await Assert.That(ansi.Contains(ESC)).IsTrue();

		// "name" should be coloured with the Function colour (#DCDCAA = rgb(220,220,170))
		await Assert.That(ansi.Contains(Foreground(0xDC, 0xDC, 0xAA))).IsTrue();

		// "%#" should be coloured with the Substitution colour (#4FC1FF = rgb(79,193,255))
		await Assert.That(ansi.Contains(Foreground(0x4F, 0xC1, 0xFF))).IsTrue();
	}

	[Test]
	public async Task RenderSharpCodeBlock_WithRealParser_ShouldApplyObjectReferenceColour()
	{
		// Arrange - sharp block with a dbref object reference
		var markdown = "```sharp\nget(#1/ATTR)\n```";
		var parser = WebAppFactoryArg.FunctionParser;

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown(markdown, 78, parser);

		// Assert - plain text preserved
		await Assert.That(result.ToPlainText()).Contains("get");
		await Assert.That(result.ToPlainText()).Contains("#1");

		// ANSI codes must be present
		await Assert.That(result.ToString().Contains(ESC)).IsTrue();
	}

	[Test]
	public async Task RenderSyntaxHighlightingDemo_WithRealParser_WritesAnsiToFile()
	{
		// Arrange - demo markdown covering all three highlighting paths
		var markdown = """
			# Syntax Highlighting Demo

			## SharpMUSH code (```sharp tag)
			```sharp
			name(%#)
			get(#1/ATTR)
			%q<myvar>
			```

			## JSON (```json tag)
			```json
			{"hello": 42, "active": true}
			```

			## Python (```python tag)
			```python
			def greet(name):
			    return "Hello, " + name
			```

			## Plain (no tag — fallback)
			```
			var x = 42;
			var y = 100;
			```
			""";

		var parser = WebAppFactoryArg.FunctionParser;

		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown(markdown, 78, parser);
		var ansi = result.ToString();

		// Write ANSI output to a temp file so the snapshot can be captured externally
		await File.WriteAllTextAsync("/tmp/sharp_demo_ansi.txt", ansi);

		// Assert - all blocks rendered
		await Assert.That(result.ToPlainText()).Contains("name");
		await Assert.That(result.ToPlainText()).Contains("hello");
		await Assert.That(result.ToPlainText()).Contains("def");

		// ANSI codes present (sharp block + json + python all coloured)
		await Assert.That(ansi.Contains(ESC)).IsTrue();

		// sharp block: "name" should be Function colour (#DCDCAA)
		await Assert.That(ansi.Contains(Foreground(0xDC, 0xDC, 0xAA))).IsTrue();
	}
}
