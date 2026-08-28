using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.ParserInterfaces;
using System.Text;

namespace SharpMUSH.Tests.Functions;

public class MarkdownFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private const string ESC = "\u001b";
	private const string Bold = "\u001b[1m";
	private const string Underlined = "\u001b[4m";
	private const string Clear = "\u001b[0m";
	private static string Foreground(byte r, byte g, byte b) => $"\u001b[38;2;{r};{g};{b}m";

	/// <summary>
	/// Helper method to perform byte-wise comparison of MarkupStrings
	/// </summary>
	private static async Task AssertMarkupStringEquals(MString actual, MString expected)
	{
		var actualBytes = Encoding.Unicode.GetBytes(actual.ToString());
		var expectedBytes = Encoding.Unicode.GetBytes(expected.ToString());

		await Assert.That(actualBytes.Length).IsEqualTo(expectedBytes.Length);

		foreach (var (actualByte, expectedByte) in actualBytes.Zip(expectedBytes))
		{
			await Assert.That(actualByte).IsEqualTo(expectedByte);
		}
	}

	/// <summary>
	/// Returns the plain-text representation of <paramref name="result"/> with
	/// trailing whitespace trimmed from every line.  Code-block lines are
	/// rendered with terminal-fill padding (spaces to fill the terminal width
	/// for background-colour spanning), so line-level <see cref="string.TrimEnd()"/>
	/// is needed when asserting indentation rather than exact character count.
	/// </summary>
	private static string TrimmedPlainText(MString result) =>
		string.Join("\n", result.ToPlainText().Split('\n').Select(l => l.TrimEnd()));

	[Test]
	public async Task RenderMarkdown_PlainText_ExactMatch()
	{
		// Note: In MUSH, strings with spaces/special chars need proper quoting or escaping
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(Hello world)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("Hello world");
	}

	[Test]
	public async Task RenderMarkdown_BoldText_ExactMatch()
	{
		var markdown = "This is **bold** text";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("This is **bold** text");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_ItalicText_ExactMatch()
	{
		// italic is rendered as bold in this implementation
		var markdown = "This is *italic* text";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("This is *italic* text");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_Heading1_ExactMatch()
	{
		var markdown = "# Heading 1";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("# Heading 1");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_InlineCode_ExactMatch()
	{
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(This is `code` text)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("This is code text");
	}

	[Test]
	public async Task RenderMarkdown_CodeBlock_ExactMatch()
	{
		// Lines are rendered with background-fill padding; use TrimmedPlainText when checking indentation.
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(```%rcode line 1%rcode line 2%r```)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(TrimmedPlainText(result!)).IsEqualTo("  code line 1\n  code line 2");
	}

	[Test]
	public async Task RenderMarkdown_CodeBlock_Indentation_ExactMatch()
	{
		// Lines are rendered with background-fill padding; use TrimmedPlainText when checking indentation.
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(```%rLine one%r  Line two indented%rLine three%r```)")))?.Message;
		await Assert.That(result).IsNotNull();

		// All lines in code blocks should have 2 spaces of indentation added.
		// Note: PennMUSH PE_COMPRESS_SPACES compresses runs of spaces in evaluated args,
		// so "  Line two indented" (2 leading spaces) becomes " Line two indented" (1 space)
		// before rendermarkdown adds its 2-space indent, giving 3 total spaces.
		var expected = "  Line one\n   Line two indented\n  Line three";
		await Assert.That(TrimmedPlainText(result!)).IsEqualTo(expected);
	}

	[Test]
	public async Task RenderMarkdown_Link_ExactMatch()
	{
		// Escape square brackets and parentheses with percent for MUSH parser
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(%[Click here%]%(https://example.com%))")))?.Message;
		await Assert.That(result).IsNotNull();
		// With hyperlink markup, only the link text is visible in plain text; the URL is embedded in ANSI escape codes
		await Assert.That(result!.ToPlainText()).IsEqualTo("Click here");
		var fullString = result.ToString();
		await Assert.That(fullString).Contains("https://example.com");
	}

	[Test]
	public async Task RenderMarkdown_Link_WithUrlOnly_ExactMatch()
	{
		// autolink: URL by itself in angle brackets, shown as both text and link
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(<https://example.com>)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("https://example.com");
		var fullString = result.ToString();
		await Assert.That(fullString).Contains("\u001b]8;;");
	}

	[Test]
	public async Task RenderMarkdown_Link_TextSameAsUrl_ExactMatch()
	{
		// Escape square brackets and parentheses with percent for MUSH parser
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(%[https://example.com%]%(https://example.com%))")))?.Message;
		await Assert.That(result).IsNotNull();
		// Link text is shown, URL is in hyperlink metadata
		await Assert.That(result!.ToPlainText()).IsEqualTo("https://example.com");
		var fullString = result.ToString();
		await Assert.That(fullString).Contains("\u001b]8;;");
	}

	[Test]
	public async Task RenderMarkdown_UnorderedList_ExactMatch()
	{
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(- Item 1%r- Item 2%r- Item 3)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("Item 1, Item 2, Item 3");
	}

	[Test]
	public async Task RenderMarkdown_OrderedList_ExactMatch()
	{
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(1. First%r2. Second%r3. Third)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("1. First\n2. Second\n3. Third");
	}

	[Test]
	public async Task RenderMarkdown_Quote_ExactMatch()
	{
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(> This is a quote)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("  This is a quote");
	}

	[Test]
	public async Task RenderMarkdown_Table_ExactMatch()
	{
		var markdown = "| Header 1 | Header 2 |%r|---|---|%r| Cell 1 | Cell 2 |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("| Header 1 | Header 2 |\n|---|---|\n| Cell 1 | Cell 2 |");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_WithCustomWidth_ExactMatch()
	{
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(| A | B | C |%r|---|---|---|%r| 1 | 2 | 3 |,50)")))?.Message;
		await Assert.That(result).IsNotNull();

		var lines = result!.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		foreach (var line in lines)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(50);
		}
	}

	[Test]
	public async Task RenderMarkdown_DefaultWidth78_ExactMatch()
	{
		var resultDefault = (await Parser.FunctionParse(MModule.single("rendermarkdown(| Column 1 | Column 2 | Column 3 |%r|---|---|---|%r| A | B | C |)")))?.Message;
		var result78 = (await Parser.FunctionParse(MModule.single("rendermarkdown(| Column 1 | Column 2 | Column 3 |%r|---|---|---|%r| A | B | C |,78)")))?.Message;

		await Assert.That(resultDefault).IsNotNull();
		await Assert.That(result78).IsNotNull();

		// Both should produce the same output
		await Assert.That(resultDefault!.ToPlainText()).IsEqualTo(result78!.ToPlainText());
	}

	[Test]
	public async Task RenderMarkdown_MultipleParagraphs_ExactMatch()
	{
		// Note: Markdown treats double newlines as paragraph breaks, which get rendered as double newlines in output
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(Paragraph 1%r%rParagraph 2)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("Paragraph 1\n\nParagraph 2");
	}

	[Test]
	public async Task RenderMarkdown_NestedEmphasis_ExactMatch()
	{
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(This is ***bold and italic*** text)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("This is bold and italic text");
	}

	[Test]
	public async Task RenderMarkdown_HtmlEntityHandling_ExactMatch()
	{
		var markdown = "&copy; 2024";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("&copy; 2024");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_HtmlTagsStripped_ExactMatch()
	{
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(This is <b>bold</b> text)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("This is bold text");
	}

	[Test]
	public async Task RenderMarkdown_EmptyInput_ExactMatch()
	{
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown()")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task RenderMarkdown_ComplexMixedContent_FullComparison()
	{
		var markdown = "# Title%r%rThis is **bold** and *italic*.%r%r- Item 1%r- Item 2";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("# Title\n\nThis is **bold** and *italic*.\n\n- Item 1\n- Item 2");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_TableWithAlignment_FullComparison()
	{
		var markdown = "| Left | Center | Right |%r|:---|:---:|---:|%r| A | B | C |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("| Left | Center | Right |\n|:---|:---:|---:|\n| A | B | C |");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_NestedListsAndQuotes_FullComparison()
	{
		var markdown = "- Item 1%r  - Nested 1%r  - Nested 2%r- Item 2%r%r> Quote text";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		// Note: PE_COMPRESS_SPACES compresses runs of spaces in evaluated args,
		// so "  - Nested" (2 spaces) becomes " - Nested" (1 space).
		var expected = RecursiveMarkdownHelper.RenderMarkdown("- Item 1\n - Nested 1\n - Nested 2\n- Item 2\n\n> Quote text");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_CodeBlocksWithLanguage_FullComparison()
	{
		// Lines are rendered with background-fill padding; use TrimmedPlainText when checking indentation.
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(```%rvar x = 42;%rvar y = 100;%r```)")))?.Message;
		await Assert.That(result).IsNotNull();

		await Assert.That(TrimmedPlainText(result!)).IsEqualTo("  var x = 42;\n  var y = 100;");
	}

	[Test]
	public async Task RenderMarkdown_MixedFormattingInParagraph_FullComparison()
	{
		var markdown = "This has **bold** text.";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("This has **bold** text.");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_HeadingsH1ThroughH3_FullComparison()
	{
		var markdown = "# H1 Heading%r## H2 Heading%r### H3 Heading";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("# H1 Heading\n## H2 Heading\n### H3 Heading");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_CompleteDocument_FullComparison()
	{
		var markdown = "# Project Title%r%rThis is a **complete** example.%r%r## Features%r%r- Item 1%r- Item 2%r%r> Important note%r%r```%rcode here%r```";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("# Project Title\n\nThis is a **complete** example.\n\n## Features\n\n- Item 1\n- Item 2\n\n> Important note\n\n```\ncode here\n```");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_OrderedListWithNumbers_FullComparison()
	{
		var markdown = "1. First item%r2. Second item%r3. Third item";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("1. First item\n2. Second item\n3. Third item");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_TableColumnSpacing_DefaultWidth()
	{
		var markdown = "| Header A | Header B | Header C |%r|---|---|---|%r| Data 1 | Data 2 | Data 3 |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var fullOutput = result!.ToString();
		var plainText = result.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		foreach (var line in lines)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(78);
		}

		await Assert.That(lines.Length).IsEqualTo(3);

		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith("|")).IsTrue();
			await Assert.That(line.EndsWith("|")).IsTrue();
		}
	}

	[Test]
	public async Task RenderMarkdown_TableColumnSpacing_CustomWidth50()
	{
		var markdown = "| Column One | Column Two | Column Three |%r|---|---|---|%r| A | B | C |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown},50)")))?.Message;
		await Assert.That(result).IsNotNull();

		var plainText = result!.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		foreach (var line in lines)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(50);
		}

		await Assert.That(lines.Length).IsEqualTo(3);

		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith("|")).IsTrue();
			await Assert.That(line.EndsWith("|")).IsTrue();
		}
	}

	[Test]
	public async Task RenderMarkdown_TableColumnAlignment_LeftCenterRight()
	{
		var markdown = "| Left | Center | Right |%r|:---|:---:|---:|%r| L | C | R |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("| Left | Center | Right |\n|:---|:---:|---:|\n| L | C | R |");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_TableExpansion_SmallContentFitsWidth()
	{
		var markdown = "| A | B |%r|---|---|%r| 1 | 2 |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var plainText = result!.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		// Small table should expand toward the default width (78)
		// It won't be exactly 78 due to cell content, but should be wider than minimal
		// At minimum, it should have proper spacing with borders
		foreach (var line in lines)
		{
			// Minimum width would be "| A | B |" = 9 chars
			// With expansion, should be significantly wider
			await Assert.That(line.Length).IsGreaterThan(15);

			await Assert.That(line.Length).IsLessThanOrEqualTo(78);
		}
	}

	[Test]
	public async Task RenderMarkdown_TableShrinking_LargeContentFitsWidth()
	{
		// Note: Table fitting/shrinking works but some constraints apply based on content
		var markdown = "| Very Long Header One | Very Long Header Two | Very Long Header Three | Very Long Header Four |%r|---|---|---|---|%r| Data1 | Data2 | Data3 | Data4 |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown},60)")))?.Message;
		await Assert.That(result).IsNotNull();

		var plainText = result!.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		await Assert.That(lines.Length).IsEqualTo(3);

		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith("|")).IsTrue();
			await Assert.That(line.EndsWith("|")).IsTrue();
		}

		var result120 = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown},120)")))?.Message;
		await Assert.That(result120).IsNotNull();

		var lines120 = result120!.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		foreach (var line in lines120)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(120);
		}
	}

	[Test]
	public async Task RenderMarkdown_TableProportionalScaling_MultipleColumns()
	{
		var markdown = "| Short | Medium Length | Very Very Long Column |%r|---|---|---|%r| A | B | C |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var plainText = result!.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

		var headerRow = lines[0];
		var cells = headerRow.Split('|', StringSplitOptions.RemoveEmptyEntries);

		await Assert.That(cells.Length).IsEqualTo(3);

		var shortWidth = cells[0].Trim().Length;
		var mediumWidth = cells[1].Trim().Length;
		var longWidth = cells[2].Trim().Length;

		await Assert.That(longWidth).IsGreaterThanOrEqualTo(mediumWidth);
		await Assert.That(mediumWidth).IsGreaterThanOrEqualTo(shortWidth);
	}

	[Test]
	public async Task RenderMarkdown_FullDocument_ByteWiseComparison()
	{
		var markdown = "# Main Title%r%rSome **bold** text here.%r%r| Col1 | Col2 |%r|---|---|%r| A | B |%r%r- List item 1%r- List item 2";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("# Main Title\n\nSome **bold** text here.\n\n| Col1 | Col2 |\n|---|---|\n| A | B |\n\n- List item 1\n- List item 2");

		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdownCustom_AllCustomTemplates_NonDefaultBehavior()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(MarkdownCustomTestObj)")))?.Message?.ToString()!;
		await Assert.That(createResult).IsNotNull();
		var testDbref = createResult.Trim();

		// Use & command instead of attrib_set() to avoid evaluating the template before storing
		var h1Set = await Parser.CommandParse(MModule.single($"&RENDERMARKUP`H1 {testDbref}=[ansi(hg,>>> %0)]"));

		var h2Set = await Parser.CommandParse(MModule.single($"&RENDERMARKUP`H2 {testDbref}=[ansi(hc,>> %0)]"));

		var h3Set = await Parser.CommandParse(MModule.single($"&RENDERMARKUP`H3 {testDbref}=[ansi(hm,> %0)]"));

		var cbSet = await Parser.CommandParse(MModule.single($"&RENDERMARKUP`CODEBLOCK {testDbref}=[ansi(hy,%[CODE:%])]%r[ansi(h,%0)]"));

		var liSet = await Parser.CommandParse(MModule.single($"&RENDERMARKUP`LISTITEM {testDbref}=[if(%0,[ansi(hr,%(%1%). %2)],[ansi(hb,★ %2)])]"));

		var qSet = await Parser.CommandParse(MModule.single($"&RENDERMARKUP`QUOTE {testDbref}=[ansi(hb,QUOTE: %0)]"));

		var markdown = "# H1 Title%r## H2 Title%r### H3 Title%r%r```%rcode line 1%rcode line 2%r```%r%r1. First item%r2. Second item%r%r- Bullet one%r- Bullet two%r%r> This is a quote";

		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdowncustom({markdown},{testDbref})")))?.Message;
		await Assert.That(result).IsNotNull();

		var plainText = result!.ToPlainText();

		await Assert.That(plainText).Contains(">>> H1 Title");

		await Assert.That(plainText).Contains(">> H2 Title");

		await Assert.That(plainText).Contains("> H3 Title");

		await Assert.That(plainText).Contains("[CODE:]");

		await Assert.That(plainText).Contains("(1). First item");
		await Assert.That(plainText).Contains("(2). Second item");

		await Assert.That(plainText).Contains("★ Bullet one");
		await Assert.That(plainText).Contains("★ Bullet two");

		await Assert.That(plainText).Contains("QUOTE: This is a quote");

		var fullString = result.ToString();

		await Assert.That(fullString).Contains("\u001b[");

		var h1Line = plainText.Split('\n').FirstOrDefault(l => l.Contains(">>> H1 Title"));
		await Assert.That(h1Line).IsNotNull();

		await Assert.That(plainText).DoesNotContain("==="); // Default H1 uses === underline
		await Assert.That(plainText).DoesNotContain("---"); // Default H2 uses --- underline (also used in tables but we don't have tables in this test)
	}

	/// <summary>
	/// <c>rendermarkdown()</c> hands its output to softcode that may present it however it likes, so a
	/// <c>[[wiki link]]</c> stays neutral styled prose here. The clickable <c>@wiki &lt;page&gt;</c>
	/// command link belongs to the <c>@wiki</c> command alone, which is the only surface that can act on
	/// it. (<c>%[</c> / <c>%]</c> escape the brackets past the MUSH parser.)
	/// </summary>
	[Test]
	public async Task RenderMarkdown_WikiLink_IsNotACommandLink()
	{
		var result = (await Parser.FunctionParse(
			MModule.single("rendermarkdown(See %[%[Getting Started%]%] for details.)")))?.Message;

		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("See Getting Started for details.");
		await Assert.That(result.Render("html")).DoesNotContain("xch_cmd");
		await Assert.That(result.Render("html")).DoesNotContain("@wiki");
		await Assert.That(result.Render("pueblo")).DoesNotContain("XCH_CMD");

		// Still underlined, so nothing about the terminal rendering changed.
		await Assert.That(result.ToString()).Contains(Underlined);
	}

	/// <summary>
	/// The inline and link templates. LINK and WIKILINK are the point of the exercise: a game that wants
	/// a markdown link to become a command its client can run needs the target and the
	/// already-a-command flag, not just the text.
	/// </summary>
	[Test]
	public async Task RenderMarkdownCustom_InlineAndLinkTemplates_Override()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(MarkdownInlineTemplateObj)")))?.Message?.ToString()!;
		await Assert.That(createResult).IsNotNull();
		var obj = createResult.Trim();

		// & rather than attrib_set() so the template is stored unevaluated.
		await Parser.CommandParse(MModule.single($"&RENDERMARKUP`BOLD {obj}=[ansi(hr,BOLD:%0)]"));
		await Parser.CommandParse(MModule.single($"&RENDERMARKUP`INLINECODE {obj}=<%0>"));
		await Parser.CommandParse(MModule.single($"&RENDERMARKUP`LINK {obj}=%0 %(%1%) cmd=%2"));
		await Parser.CommandParse(MModule.single($"&RENDERMARKUP`WIKILINK {obj}=[ansi(hc,%0)] %(@wiki %1%)"));

		// No commas: rendermarkdowncustom()'s second argument is the template object, so a comma in
		// the markdown would be read as one (and the third as a width).
		var markdown = "A **strong** `word`.%r%rSee %[%[Help:Markdown Guide%]%] and "
			+ "%[Site%]%(https://example.com%) and %[newbie%].";

		var result = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj})")))?.Message;
		await Assert.That(result).IsNotNull();

		var plainText = result!.ToPlainText();

		await Assert.That(plainText).Contains("BOLD:strong");
		await Assert.That(plainText).Contains("<word>");

		// A URL link and a help-topic shortcut reach the same template and are told apart by %2.
		await Assert.That(plainText).Contains("Site (https://example.com) cmd=0");
		await Assert.That(plainText).Contains("newbie (help newbie) cmd=1");

		// WIKILINK's %1 is the fully qualified @wiki reference, whatever short form was written.
		// This is the example in `help rendermarkdowncustom`, verbatim.
		await Assert.That(plainText).Contains("Markdown Guide (@wiki help:general:markdown_guide)");
	}

	/// <summary>
	/// Every template is optional and absent ones change nothing: an object with no
	/// <c>RENDERMARKUP`</c> attributes at all must render byte-for-byte like <c>rendermarkdown()</c>,
	/// including for the elements that only gained a template hook now.
	/// </summary>
	[Test]
	public async Task RenderMarkdownCustom_NoTemplates_MatchesDefaultRenderingForNewHooks()
	{
		var createResult = (await Parser.FunctionParse(MModule.single("create(MarkdownNoTemplateObj)")))?.Message?.ToString()!;
		await Assert.That(createResult).IsNotNull();
		var obj = createResult.Trim();

		var markdown = "A **strong** `word` and %[Site%]%(https://example.com%) "
			+ "and %[%[Help:Markdown Guide%]%].%r%r- %[x%] done%r- %[ %] todo";

		var result = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown(
			"A **strong** `word` and [Site](https://example.com) "
			+ "and [[Help:Markdown Guide]].\n\n- [x] done\n- [ ] todo");

		await AssertMarkupStringEquals(result!, expected);
	}

	/// <summary>Creates a fresh object to hang RENDERMARKUP templates on.</summary>
	private async Task<string> CreateTemplateObject(string name)
	{
		var created = (await Parser.FunctionParse(MModule.single($"create({name})")))?.Message?.ToString()!;
		await Assert.That(created).IsNotNull();
		return created.Trim();
	}

	/// <summary>
	/// TABLE hands over the table described as one JSON object, not the finished block. That is what
	/// lets a template lay a table out in a game's own style rather than only recolour the built-in one.
	/// </summary>
	[Test]
	public async Task RenderMarkdownCustom_TableTemplate_ReceivesTheTableAsJson()
	{
		var obj = await CreateTemplateObject("MarkdownTableJsonObj");
		await Parser.CommandParse(MModule.single($"&RENDERMARKUP`TABLE {obj}=%0"));

		// The last row writes one cell where the header has three.
		var markdown = "| A | B | C |%r|:---|:---:|---:|%r| 1 | 2 | 3 |%r| 4 |";

		var result = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj})")))?.Message;
		await Assert.That(result).IsNotNull();

		await Assert.That(result!.ToPlainText().Trim()).IsEqualTo(
			"""{"width":78,"align":["<","-",">"],"widths":[22,22,22],"head":["A","B","C"],"rows":[["1","2","3"],["4","",""]]}""");
	}

	/// <summary>
	/// <c>width</c> is the width the <em>caller</em> asked this render for, not the reader's terminal
	/// width — a fixed-width export has to get a table that agrees with the prose around it.
	/// </summary>
	[Test]
	public async Task RenderMarkdownCustom_TableTemplate_WidthIsTheRequestedRenderWidth()
	{
		var obj = await CreateTemplateObject("MarkdownTableWidthObj");
		await Parser.CommandParse(MModule.single($"&RENDERMARKUP`TABLE {obj}=W:[json_query(%0,get,width)]"));

		var markdown = "| A |%r|---|%r| 1 |";

		var wide = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj},120)")))?.Message;
		var narrow = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj},40)")))?.Message;

		await Assert.That(wide!.ToPlainText()).Contains("W:120");
		await Assert.That(narrow!.ToPlainText()).Contains("W:40");
	}

	/// <summary>
	/// The cells are markdown <em>source</em>, not a rendering. That is the contract: a template decides
	/// whether to call <c>rendermarkdown()</c> on a cell, so the formatting choice stays with the game.
	/// </summary>
	[Test]
	public async Task RenderMarkdownCustom_TableTemplate_CellsAreRawSourceNotRendered()
	{
		var obj = await CreateTemplateObject("MarkdownTableRawSourceObj");
		await Parser.CommandParse(MModule.single(
			$"&RENDERMARKUP`TABLE {obj}=RAW:[json_query(%0,extract,$.head%[0%])]"
			+ " OUT:[rendermarkdown([json_query(%0,extract,$.head%[0%])])]"));

		var markdown = "| **loud** |%r|---|%r| x |";

		var result = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj})")))?.Message;
		await Assert.That(result).IsNotNull();

		var plainText = result!.ToPlainText();

		// Verbatim: the asterisks are still there and nothing has been styled for us.
		await Assert.That(plainText).Contains("RAW:**loud**");
		// And the template can render the cell itself, which is where styling comes from.
		await Assert.That(plainText).Contains("OUT:loud");
		await Assert.That(result.ToString()).Contains(Bold);
	}

	/// <summary>
	/// JSON, not a delimited list, so a cell holding a quote or a backslash round-trips instead of
	/// silently shifting the columns. A literal pipe is <c>\|</c> in table source and is passed through
	/// as written — <c>rendermarkdown()</c> is what resolves it.
	/// </summary>
	[Test]
	public async Task RenderMarkdownCustom_TableTemplate_EscapesRoundTripThroughJsonQuery()
	{
		var obj = await CreateTemplateObject("MarkdownTableEscapeObj");
		await Parser.CommandParse(MModule.single(
			$"&RENDERMARKUP`TABLE {obj}=COLS:[json_query([json_query(%0,get,widths)],size)] RAW:%0"
			+ " CELL:[json_query(%0,extract,$.head%[0%])]"
			+ " OUT:[rendermarkdown([json_query(%0,extract,$.head%[0%])])]"));

		// MUSH collapses "\\" to one backslash, so markdown sees: | say "hi" \| pipe |
		var markdown = "| say \"hi\" \\\\| pipe |%r|---|%r| x |";

		var result = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj})")))?.Message;
		await Assert.That(result).IsNotNull();

		var plainText = result!.ToPlainText();

		await Assert.That(plainText)
			.Contains("COLS:1")
			.Because("the escaped pipe is cell content and must not have opened a second column");
		// Encoded in the payload: the quote and the backslash both escaped.
		await Assert.That(plainText).Contains("\\\"hi\\\"");
		await Assert.That(plainText).Contains("\\\\|");
		// Decoded back to exactly the source the author wrote, escapes and all.
		await Assert.That(plainText)
			.Contains("CELL:say \"hi\" \\| pipe")
			.Because("the \\| is left as written; unescaping it here would make it indistinguishable "
				+ "from a cell boundary");
		// rendermarkdown() is what turns the escape into the character.
		await Assert.That(plainText).Contains("OUT:say \"hi\" | pipe");
	}

	/// <summary>
	/// The worked example from <c>help rendermarkdowncustom</c>, run verbatim, so the documented output
	/// is the output. Demonstrates what the JSON payload is for: <c>json_map()</c> walks <c>rows</c>,
	/// each row's helper walks its own cells, and the layout is the game's.
	/// </summary>
	[Test]
	public async Task RenderMarkdownCustom_TableTemplate_RebuildsTheTableWithLalign()
	{
		var obj = await CreateTemplateObject("MarkdownTableAlignObj");

		// Helpers are addressed by dbref, not "me": a RENDERMARKUP template is evaluated with the
		// *caller* as executor, so "me" inside one is the caller, not the object holding the templates.
		foreach (var attribute in new[]
		{
			"&FUN`VAL {0}=%1",
			"&FUN`TABLE`SPEC {0}=[json_query(%1,unescape)][json_query(%q<wd>,get,%2)]",
			"&FUN`TABLE`CELL {0}=[rendermarkdown([json_query(%1,unescape)],[max(10,json_query(%q<wd>,get,%2))])]",
			"&FUN`TABLE`ROW {0}=[lalign(%q<spec>,[json_map({0}/FUN`TABLE`CELL,%0,|)],|)]",
			"&FUN`TABLE`ROWJ {0}=[u({0}/FUN`TABLE`ROW,%1)]",
			"&FUN`TABLE`HEAD {0}=[ansi(hw,[u({0}/FUN`TABLE`ROW,[json_query(%0,get,head)])])]",
			"&FUN`TABLE`RULE {0}=[repeat(-,%q<tw>)]",
			"&FUN`TABLE`BODY {0}=[json_map({0}/FUN`TABLE`ROWJ,[json_query(%0,get,rows)],%r)]",
			"&RENDERMARKUP`TABLE {0}=[setq(wd,json_query(%0,get,widths))]"
				+ "[setq(spec,json_map({0}/FUN`TABLE`SPEC,[json_query(%0,get,align)],%b))]"
				+ "[setq(tw,add(lmath(add,[json_map({0}/FUN`VAL,%q<wd>,%b)]),sub(json_query(%q<wd>,size),1)))]"
				+ "[jiter({0}/FUN`TABLE`HEAD {0}/FUN`TABLE`RULE {0}/FUN`TABLE`BODY,%0,%r)]",
		})
		{
			await Parser.CommandParse(MModule.single(string.Format(attribute, obj)));
		}

		var markdown = "| Command | Effect | Cost |%r|:---|:-:|--:|"
			+ "%r| @wiki | Show the **index** | 0 |"
			+ "%r| @wiki/search | Find pages | 1 |"
			+ "%r| @wiki/audit | Staff report |";

		var result = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj},40)")))?.Message;
		await Assert.That(result).IsNotNull();

		var lines = result!.ToPlainText().Split('\n').Select(line => line.TrimEnd()).ToList();
		// Exactly what the helpfile prints for this template, at the columns the payload's own widths
		// give — measured from the rendered cells, which is the thing a template cannot do for itself.
		await Assert.That(lines).IsEquivalentTo(["Command          Effect     Cost", "--------------------------------", "@wiki        Show the index    0", "@wiki/search   Find pages      1", "@wiki/audit   Staff report"]);
		// The cell's markdown was rendered by the template, so "index" really is bold.
		await Assert.That(result.ToString()).Contains(Bold);
	}

	/// <summary>
	/// A table on an object with no TABLE template renders exactly as <c>rendermarkdown()</c> would, and
	/// no payload is built at all — the template is looked up before the table is taken apart.
	/// </summary>
	[Test]
	public async Task RenderMarkdownCustom_NoTableTemplate_MatchesDefaultRendering()
	{
		var obj = await CreateTemplateObject("MarkdownNoTableTemplateObj");

		var markdown = "| A | B |%r|---|---|%r| 1 | 2 |";

		var result = (await Parser.FunctionParse(
			MModule.single($"rendermarkdowncustom({markdown},{obj})")))?.Message;
		await Assert.That(result).IsNotNull();

		var expected = RecursiveMarkdownHelper.RenderMarkdown("| A | B |\n|---|---|\n| 1 | 2 |");

		await AssertMarkupStringEquals(result!, expected);
	}
}
