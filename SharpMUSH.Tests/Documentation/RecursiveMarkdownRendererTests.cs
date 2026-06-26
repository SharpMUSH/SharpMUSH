namespace SharpMUSH.Tests.Documentation;

public class RecursiveMarkdownRendererTests
{
	private const string ESC = "\u001b";
	private const string CSI = "\u001b[";
	private const string Bold = "\u001b[1m";
	private const string Faint = "\u001b[2m";
	private const string Italic = "\u001b[3m";
	private const string Underlined = "\u001b[4m";
	private const string StrikeThrough = "\u001b[9m";
	private const string Clear = "\u001b[0m";
	private static string Foreground(byte r, byte g, byte b) => $"\u001b[38;2;{r};{g};{b}m";

	[Test]
	public async Task RenderPlainText_ShouldWork()
	{
		var markdown = "Hello, world!";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hello, world!");
		await Assert.That(result.ToString()).IsEqualTo("Hello, world!");
	}

	[Test]
	public async Task RenderBoldText_ShouldApplyMarkup()
	{
		var markdown = "This is **bold** text";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("This is bold text");

		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains(Foreground(255, 255, 255))).IsTrue();
		var boldIndex = fullString.IndexOf("bold");
		var ansiIndex = fullString.IndexOf(Bold);
		await Assert.That(ansiIndex).IsLessThan(boldIndex);
	}

	[Test]
	public async Task RenderItalicText_ShouldApplyMarkup()
	{
		var markdown = "This is *italic* text";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// italic is rendered as bold in this implementation
		await Assert.That(result.ToPlainText()).IsEqualTo("This is italic text");

		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains(Foreground(255, 255, 255))).IsTrue();
		var italicIndex = fullString.IndexOf("italic");
		var ansiIndex = fullString.IndexOf(Bold);
		await Assert.That(ansiIndex).IsLessThan(italicIndex);
	}

	[Test]
	public async Task RenderTable_WithMaxWidth_ShouldConstrainColumns()
	{
		var markdown = @"| Column1 | Column2 | Column3 |
| --- | --- | --- |
| A | B | C |";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown, maxWidth: 50);

		var plainText = result.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		foreach (var line in lines)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(50);
		}

		await Assert.That(plainText.Contains("Column1")).IsTrue();
		await Assert.That(plainText.Contains("Column2")).IsTrue();
		await Assert.That(plainText.Contains("Column3")).IsTrue();
	}

	[Test]
	public async Task RenderTable_WithAlignment_ShouldUseAlignFunction()
	{
		var markdown = @"| Left | Center | Right |
| :--- | :---: | ---: |
| L1 | C1 | R1 |";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		var plainText = result.ToPlainText();
		await Assert.That(plainText.Length).IsGreaterThan(0);

		await Assert.That(plainText.Contains("Left")).IsTrue();
		await Assert.That(plainText.Contains("Center")).IsTrue();
		await Assert.That(plainText.Contains("Right")).IsTrue();
		await Assert.That(plainText.Contains("L1")).IsTrue();
		await Assert.That(plainText.Contains("C1")).IsTrue();
		await Assert.That(plainText.Contains("R1")).IsTrue();

		await Assert.That(result.ToString().Contains(Faint)).IsTrue();
	}

	[Test]
	public async Task RenderList_WithBullets_ShouldRenderAsCommaSeparated()
	{
		var markdown = "- Item 1\n- Item 2";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		// unordered lists are displayed as comma-separated values
		await Assert.That(result.ToPlainText()).IsEqualTo("Item 1, Item 2");
		await Assert.That(result.ToString().Contains("Item 1")).IsTrue();
		await Assert.That(result.ToString().Contains("Item 2")).IsTrue();
	}

	[Test]
	public async Task RenderOrderedList_ShouldHaveFaintNumbers()
	{
		var markdown = "1. First\n2. Second";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("1. First\n2. Second");
		await Assert.That(result.ToString().Contains(Faint)).IsTrue();
	}

	[Test]
	public async Task RenderHeading1_ShouldHaveUnderlineAndBold()
	{
		var markdown = "# Heading 1";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 1");
		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Underlined)).IsTrue();
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains("Heading 1")).IsTrue();
	}

	[Test]
	public async Task RenderHeading2_ShouldHaveUnderlineAndBold()
	{
		var markdown = "## Heading 2";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 2");
		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Underlined)).IsTrue();
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains("Heading 2")).IsTrue();
	}

	[Test]
	public async Task RenderHeading3_ShouldHaveUnderline()
	{
		var markdown = "### Heading 3";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 3");
		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Underlined)).IsTrue();
		await Assert.That(fullString.Contains("Heading 3")).IsTrue();
	}

	[Test]
	public async Task RenderCodeBlock_ShouldPreserveContent()
	{
		var markdown = "```\ncode line 1\ncode line 2\n```";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).Contains("code line 1");
		await Assert.That(result.ToPlainText()).Contains("code line 2");
		await Assert.That(result.ToString().Contains(ESC)).IsTrue();
	}

	[Test]
	public async Task RenderCodeBlock_UnlabeledFenced_ShouldApplyBackground()
	{
		var markdown = "```\nhello world\n```";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).Contains("hello world");
		await Assert.That(result.ToString()).Contains("\u001b[48;2;45;45;45m");
	}

	[Test]
	public async Task RenderInlineCode_ShouldPreserveContent()
	{
		var markdown = "This is `inline code` here";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("This is inline code here");
		await Assert.That(result.ToString().Contains(ESC)).IsTrue();
	}

	[Test]
	public async Task RenderInlineCode_ShouldApplyLightBlueColor()
	{
		var markdown = "Use `name()` to get the name";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("Use name() to get the name");
		await Assert.That(result.ToString()).Contains(Foreground(0x9C, 0xDC, 0xFE));
	}

	[Test]
	public async Task RenderQuote_ShouldIndent()
	{
		var markdown = "> This is a quote";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		var plainText = result.ToPlainText();
		await Assert.That(plainText).IsEqualTo("  This is a quote");
	}

	[Test]
	public async Task RenderLink_ShouldShowTextAndUrl()
	{
		var markdown = "[Link Text](https://example.com)";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("Link Text");
		var fullString = result.ToString();
		await Assert.That(fullString).Contains("https://example.com");
		await Assert.That(fullString).Contains("\u001b]8;;");
	}

	[Test]
	public async Task RenderTable_ShouldFitToWidth()
	{
		var markdown = @"| A | B |
|---|---|
| 1 | 2 |";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var lines = result.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

		var firstLineLength = lines[0].Length;
		await Assert.That(firstLineLength).IsGreaterThan(20);
		await Assert.That(firstLineLength).IsLessThanOrEqualTo(78);

		foreach (var line in lines.Where(l => !l.Contains("---")))
		{
			await Assert.That(line.Length).IsEqualTo(firstLineLength);
		}
	}

	[Test]
	public async Task RenderTable_DefaultWidthIs78()
	{
		var markdown = @"| Column 1 | Column 2 | Column 3 |
|---|---|---|
| A | B | C |";

		var resultDefault = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var linesDefault = resultDefault.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

		var result78 = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown, maxWidth: 78);
		var lines78 = result78.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);

		await Assert.That(linesDefault[0].Length).IsEqualTo(lines78[0].Length);
		await Assert.That(resultDefault.ToPlainText()).IsEqualTo(result78.ToPlainText());
	}

	[Test]
	public async Task RenderHtml_BoldTag_ShouldApplyMarkup()
	{
		var markdown = "This is <b>bold</b> text";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("This is bold text");
		await Assert.That(result.ToString().Contains(Bold)).IsTrue();
	}

	[Test]
	public async Task RenderHtml_ItalicTag_ShouldApplyMarkup()
	{
		var markdown = "This is <i>italic</i> text";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("This is italic text");
		await Assert.That(result.ToString().Contains(Italic)).IsTrue();
	}

	[Test]
	public async Task RenderHtml_UnderlineTag_ShouldApplyMarkup()
	{
		var markdown = "This is <u>underlined</u> text";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("This is underlined text");
		await Assert.That(result.ToString().Contains(Underlined)).IsTrue();
	}

	[Test]
	public async Task RenderHtml_FontColorTag_ShouldApplyMarkup()
	{
		var markdown = @"This is <font color=""red"">red</font> text";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("This is red text");
		await Assert.That(result.ToString().Contains(Foreground(255, 0, 0))).IsTrue();
	}

	[Test]
	public async Task RenderHtml_SpanWithStyle_ShouldApplyMarkup()
	{
		var markdown = @"This is <span style=""color: #FF0000"">red</span> text";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("This is red text");
		await Assert.That(result.ToString().Contains(Foreground(255, 0, 0))).IsTrue();
	}

	[Test]
	public async Task RenderHtml_BrTag_ShouldRenderAsNewline()
	{
		// <br> is a self-closing void element within a paragraph
		var markdown = "Line one<br>Line two";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("Line one\nLine two");
	}

	[Test]
	public async Task RenderHtmlBlock_ShouldPreserveContent()
	{
		// <div> is a block-level tag, parsed as HtmlBlock by Markdig.
		// HtmlBlock is a LeafBlock — Markdig doesn't recurse into it, so the
		// raw HTML is passed through as-is.
		var markdown = "<div>block content</div>";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("<div>block content</div>");
	}

	[Test]
	public async Task RenderCodeBlock_WithJsonLanguage_ShouldApplyAnsiColours()
	{
		var markdown = "```json\n{\"hello\": 42}\n```";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var ansi = result.ToString();

		await Assert.That(result.ToPlainText().Trim()).Contains("hello");
		await Assert.That(result.ToPlainText().Trim()).Contains("42");

		await Assert.That(ansi.Contains(ESC)).IsTrue();

		// The opening brace must appear without an immediately preceding ANSI colour code.
		await Assert.That(ansi).Contains("{");
		// The brace must NOT be inside an ANSI colour sequence — it should appear either right
		// after the 2-space indent or right after a reset code, never inside a colour span.
		await Assert.That(ansi).DoesNotContain(Foreground(0xFF, 0x87, 0x00) + "{");
	}

	[Test]
	public async Task RenderCodeBlock_WithPythonLanguage_ShouldApplyAnsiColours()
	{
		var markdown = "```python\ndef hello():\n    return 42\n```";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).Contains("def");
		await Assert.That(result.ToPlainText()).Contains("hello");

		await Assert.That(result.ToString().Contains(ESC)).IsTrue();
	}

	[Test]
	public async Task RenderCodeBlock_WithUnknownLanguage_ShouldFallBackToPlainText()
	{
		var markdown = "```xyz_unknown_language\nsome code here\n```";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText().Trim()).Contains("some code here");
		await Assert.That(result.ToString().Contains(ESC)).IsFalse();
	}

	[Test]
	public async Task RenderCodeBlock_WithNoLanguageTag_ShouldFallBackToPlainText()
	{
		// indented code block (no language tag)
		var markdown = "    plain indented code";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText().Trim()).Contains("plain indented code");
		await Assert.That(result.ToString().Contains(ESC)).IsFalse();
	}

	[Test]
	public async Task RenderHelpTopicLink_ShouldCreateCommandLink()
	{
		// bare [topic] with no URL definition is parsed by HelpTopicInlineParser
		// into a command LinkInline whose URL is "help <topic>".
		var markdown = "See [newbie2] for more.";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("See newbie2 for more.");

		// It is a command link: HTML renders xch_cmd (not href), ANSI has no OSC 8 link.
		await Assert.That(result.Render("html")).Contains("xch_cmd=\"help newbie2\"");
		await Assert.That(result.ToString()).DoesNotContain("]8;;");
	}

	[Test]
	public async Task RenderRegularLink_ShouldBeNavigationLink()
	{
		// an explicit [text](url) link is a navigation link, not a command.
		var markdown = "[Site](https://example.com)";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.Render("html")).Contains("href=\"https://example.com\"");
		await Assert.That(result.Render("html")).Contains("target=\"_blank\"");
		await Assert.That(result.Render("html")).DoesNotContain("xch_cmd");
	}

	[Test]
	public async Task RenderLink_WithTitle_CarriesHint()
	{
		// markdown link title becomes the link hint (HTML title / xch_hint / MXP HINT).
		var markdown = "[Site](https://example.com \"Open the site\")";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.Render("html")).Contains("title=\"Open the site\"");
	}

	[Test]
	public async Task RenderHelpTopicLink_NormalLinkUnchanged()
	{
		// [text](url) must NOT be treated as a help topic link
		var markdown = "See [topic](https://example.com) for more.";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("See topic for more.");
		await Assert.That(result.ToString()).Contains("https://example.com");
		await Assert.That(result.ToString()).DoesNotContain("help topic");
	}

	[Test]
	public async Task RenderTable_WithEmptyHeaders_ShouldBeWithoutBorders()
	{
		// table whose header row has all empty cells (like the COMMANDS list)
		var markdown =
			"|              |              |\n" +
			"|--------------|--------------|  \n" +
			"| CmdA         | CmdB         |\n" +
			"| CmdC         | CmdD         |";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var plainText = result.ToPlainText();

		await Assert.That(plainText).DoesNotContain("|");
		await Assert.That(plainText).Contains("CmdA");
		await Assert.That(plainText).Contains("CmdB");
		await Assert.That(plainText).Contains("CmdC");
		await Assert.That(plainText).Contains("CmdD");
	}

	[Test]
	public async Task RenderTable_WithNonEmptyHeaders_ShouldHaveBorders()
	{
		// table whose header row has cell content (should keep bordered format)
		var markdown =
			"| Col1 | Col2 |\n" +
			"|------|------|\n" +
			"| A    | B    |";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var plainText = result.ToPlainText();

		await Assert.That(plainText).Contains("|");
		await Assert.That(plainText).Contains("Col1");
		await Assert.That(plainText).Contains("Col2");
		await Assert.That(plainText).Contains("A");
		await Assert.That(plainText).Contains("B");
	}

	[Test]
	public async Task RenderHeading_H1_ShouldApplyBoldUnderlineWhite()
	{
		var markdown = "# test";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("test");

		var ansi = result.ToString();
		await Assert.That(ansi.Contains(Bold)).IsTrue();
		await Assert.That(ansi.Contains(Underlined)).IsTrue();
		await Assert.That(ansi.Contains(Foreground(255, 255, 255))).IsTrue();
		var textIdx = ansi.IndexOf("test");
		await Assert.That(textIdx).IsGreaterThan(ansi.IndexOf(Bold));
		await Assert.That(textIdx).IsGreaterThan(ansi.IndexOf(Underlined));
		await Assert.That(textIdx).IsGreaterThan(ansi.IndexOf(Foreground(255, 255, 255)));
	}

	[Test]
	public async Task RenderHeading_H2_ShouldApplyBoldUnderlineWhite()
	{
		var markdown = "## heading two";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("heading two");

		var ansi = result.ToString();
		await Assert.That(ansi.Contains(Bold)).IsTrue();
		await Assert.That(ansi.Contains(Underlined)).IsTrue();
		await Assert.That(ansi.Contains(Foreground(255, 255, 255))).IsTrue();
		var textIdx = ansi.IndexOf("heading two");
		await Assert.That(textIdx).IsGreaterThan(ansi.IndexOf(Bold));
		await Assert.That(textIdx).IsGreaterThan(ansi.IndexOf(Underlined));
		await Assert.That(textIdx).IsGreaterThan(ansi.IndexOf(Foreground(255, 255, 255)));
	}

	[Test]
	public async Task RenderHeading_H3_ShouldApplyUnderlineWhiteWithoutBold()
	{
		var markdown = "### heading three";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("heading three");

		var ansi = result.ToString();
		await Assert.That(ansi.Contains(Underlined)).IsTrue();
		await Assert.That(ansi.Contains(Foreground(255, 255, 255))).IsTrue();
		var textIdx = ansi.IndexOf("heading three");
		await Assert.That(textIdx).IsGreaterThan(ansi.IndexOf(Underlined));
		await Assert.That(textIdx).IsGreaterThan(ansi.IndexOf(Foreground(255, 255, 255)));
	}

	[Test]
	public async Task RenderHeading_H4_ShouldRenderPlainText()
	{
		var markdown = "#### heading four";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("heading four");
	}

	/// <summary>
	/// A level-1 heading must render with bold AND a colour (white foreground),
	/// matching <c>_headingStyle = Ansi.Create(foreground: RGB(White), underlined: true, bold: true)</c>.
	/// </summary>
	[Test]
	public async Task MarkdownToMString_Header_BoldColored()
	{
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown("# Section Title");

		var fullString = result.ToString();

		await Assert.That(result.ToPlainText()).IsEqualTo("Section Title");
		await Assert.That(fullString.Contains(Bold)).IsTrue();
		await Assert.That(fullString.Contains(Foreground(255, 255, 255))).IsTrue();
	}

	/// <summary>
	/// <c>**bold**</c> and <c>_italic_</c> must both produce ANSI equivalents —
	/// both map to the bold-white style in this renderer.
	/// </summary>
	[Test]
	public async Task MarkdownToMString_BoldItalic_AnsiEquivalents()
	{
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown("**bold** _italic_");

		var fullString = result.ToString();
		var plainText = result.ToPlainText();

		await Assert.That(plainText.Contains("bold")).IsTrue();
		await Assert.That(plainText.Contains("italic")).IsTrue();
		await Assert.That(fullString.Contains("**")).IsFalse();
		await Assert.That(fullString.Contains("_italic_")).IsFalse();

		await Assert.That(fullString.Contains(Bold)).IsTrue();
	}

	/// <summary>
	/// A Markdown table must render as fixed-width aligned text:
	/// each line must stay within <c>maxWidth</c> and all cell values must appear.
	/// </summary>
	[Test]
	public async Task MarkdownToMString_Table_FixedWidthAligned()
	{
		var markdown = @"| Name    | Role   | Level |
| ---     | ---    | ---   |
| Arthas  | Paladin | 60   |
| Jaina   | Mage    | 58   |";

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown(markdown, maxWidth: 60);

		var plainText = result.ToPlainText();
		var lines = plainText.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

		foreach (var line in lines)
			await Assert.That(line.Length).IsLessThanOrEqualTo(60);

		await Assert.That(plainText.Contains("Name")).IsTrue();
		await Assert.That(plainText.Contains("Arthas")).IsTrue();
		await Assert.That(plainText.Contains("Jaina")).IsTrue();

		await Assert.That(result.ToString().Contains(Faint)).IsTrue();
	}

	/// <summary>
	/// Images in Markdown are not renderable as ANSI graphics in a terminal/MUSH
	/// context.  The renderer should emit a faint <c>[image: alt text]</c>
	/// placeholder so the reader at least knows an image was here.
	/// </summary>
	[Test]
	public async Task MarkdownToMString_Image_FallsBackToAltTextPlaceholder()
	{
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown("![A cute cat](https://example.com/cat.jpg)", maxWidth: 78);

		var plainText = result.ToPlainText();
		await Assert.That(plainText).Contains("[image: A cute cat]");
	}

	/// <summary>
	/// An image with no alt text should still produce a sensible placeholder.
	/// </summary>
	[Test]
	public async Task MarkdownToMString_ImageNoAlt_FallsBackToImagePlaceholder()
	{
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown("![](https://example.com/bg.png)", maxWidth: 78);

		var plainText = result.ToPlainText();
		await Assert.That(plainText).Contains("[image]");
	}
}

/// <summary>
/// These tests require the full server DI stack to produce a <see cref="IMUSHCodeParser"/>.
/// </summary>
public class RecursiveMarkdownRendererWithParserTests
{
	private const string ESC = "\u001b";
	private static string Foreground(byte r, byte g, byte b) => $"\u001b[38;2;{r};{g};{b}m";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	[Test]
	public async Task RenderSharpCodeBlock_WithRealParser_ShouldApplyFunctionColour()
	{
		var markdown = "```sharp\nname(%#)\n```";
		var parser = WebAppFactoryArg.FunctionParser;

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown(markdown, 78, parser);
		var ansi = result.ToString();

		await Assert.That(result.ToPlainText()).Contains("name");
		await Assert.That(result.ToPlainText()).Contains("%#");

		await Assert.That(ansi.Contains(ESC)).IsTrue();

		await Assert.That(ansi.Contains(Foreground(0xDC, 0xDC, 0xAA))).IsTrue();

		await Assert.That(ansi.Contains(Foreground(0x4F, 0xC1, 0xFF))).IsTrue();
	}

	[Test]
	public async Task RenderSharpCodeBlock_WithRealParser_ShouldApplyObjectReferenceColour()
	{
		var markdown = "```sharp\nget(#1/ATTR)\n```";
		var parser = WebAppFactoryArg.FunctionParser;

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown(markdown, 78, parser);

		await Assert.That(result.ToPlainText()).Contains("get");
		await Assert.That(result.ToPlainText()).Contains("#1");

		await Assert.That(result.ToString().Contains(ESC)).IsTrue();
	}

	[Test]
	public async Task RenderCommandLines_WithRealParser_WritesAnsiToFile()
	{
		var markdown = """
			# SharpMUSH Command Highlighting Demo

			```sharp
			&CHECKS me=@assert [orflags(%#,Wr)]; @break [gt(words(lwho()),%0)]
			&CMD1 me=$cmd *: @include me/CHECKS; @pemit %#=You passed.
			&CMD2 me=$othercmd *: @include me/CHECKS; @@ Do something else...
			```
			""";

		var parser = WebAppFactoryArg.FunctionParser;

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown(markdown, 78, parser);
		var ansi = result.ToString();

		// Write ANSI output so the terminal screenshot can be taken
		await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "sharp_commands_demo.txt"), ansi);

		await Assert.That(result.ToPlainText()).Contains("@assert");
		await Assert.That(result.ToPlainText()).Contains("@break");
		await Assert.That(result.ToPlainText()).Contains("@pemit");

		await Assert.That(ansi.Contains(ESC)).IsTrue();
	}

	[Test]
	public async Task RenderSyntaxHighlightingDemo_WithRealParser_WritesAnsiToFile()
	{
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

		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper
			.RenderMarkdown(markdown, 78, parser);
		var ansi = result.ToString();

		// Write ANSI output to a temp file so the snapshot can be captured externally
		await File.WriteAllTextAsync(Path.Combine(Path.GetTempPath(), "sharp_demo_ansi.txt"), ansi);

		await Assert.That(result.ToPlainText()).Contains("name");
		await Assert.That(result.ToPlainText()).Contains("hello");
		await Assert.That(result.ToPlainText()).Contains("def");

		await Assert.That(ansi.Contains(ESC)).IsTrue();

		await Assert.That(ansi.Contains(Foreground(0xDC, 0xDC, 0xAA))).IsTrue();
	}
}
