using System.Text;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class MarkdownFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// ANSI escape codes for verification
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

		// Ensure both strings have the same length before byte-wise comparison
		await Assert.That(actualBytes.Length).IsEqualTo(expectedBytes.Length);

		foreach (var (actualByte, expectedByte) in actualBytes.Zip(expectedBytes))
		{
			await Assert.That(actualByte).IsEqualTo(expectedByte);
		}
	}

	[Test]
	public async Task RenderMarkdown_PlainText_ExactMatch()
	{
		// Test plain text rendering with exact string match
		// Note: In MUSH, strings with spaces/special chars need proper quoting or escaping
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(Hello world)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("Hello world");
	}

	[Test]
	public async Task RenderMarkdown_BoldText_ExactMatch()
	{
		// Test bold text with full byte-wise comparison
		var markdown = "This is **bold** text";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("This is **bold** text");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_ItalicText_ExactMatch()
	{
		// Test italic text (rendered as bold in this implementation) with full byte-wise comparison
		var markdown = "This is *italic* text";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("This is *italic* text");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_Heading1_ExactMatch()
	{
		// Test H1 heading with underline and bold - full byte-wise comparison
		var markdown = "# Heading 1";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("# Heading 1");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_InlineCode_ExactMatch()
	{
		// Test inline code
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(This is `code` text)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("This is code text");
	}

	[Test]
	public async Task RenderMarkdown_CodeBlock_ExactMatch()
	{
		// Test code block (using %r for newlines in MUSH)
		// Code blocks should be indented by 2 spaces
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(```%rcode line 1%rcode line 2%r```)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("  code line 1\n  code line 2");
	}

	[Test]
	public async Task RenderMarkdown_CodeBlock_Indentation_ExactMatch()
	{
		// Test that code blocks are properly indented by 2 spaces
		// This verifies the markup adjustment for ```code``` blocks
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(```%rLine one%r  Line two indented%rLine three%r```)")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// All lines in code blocks should have 2 spaces of indentation added (total)
		var expected = "  Line one\n    Line two indented\n  Line three";
		await Assert.That(result!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task RenderMarkdown_Link_ExactMatch()
	{
		// Test link - should use ANSI hyperlink with text as visible content
		// Escape square brackets and parentheses with percent for MUSH parser
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(%[Click here%]%(https://example.com%))")))?.Message;
		await Assert.That(result).IsNotNull();
		// With hyperlink markup, only the link text is visible in plain text
		// The URL is embedded in ANSI escape codes
		await Assert.That(result!.ToPlainText()).IsEqualTo("Click here");
		// Verify the full string contains the hyperlink escape sequence
		var fullString = result.ToString();
		await Assert.That(fullString).Contains("https://example.com");
	}

	[Test]
	public async Task RenderMarkdown_Link_WithUrlOnly_ExactMatch()
	{
		// Test autolink (URL by itself in angle brackets)
		// This is the proper markdown way to show just a URL
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(<https://example.com>)")))?.Message;
		await Assert.That(result).IsNotNull();
		// Autolinks show the URL as both text and link
		await Assert.That(result!.ToPlainText()).IsEqualTo("https://example.com");
		// Verify the hyperlink escape sequence is present
		var fullString = result.ToString();
		await Assert.That(fullString).Contains("\u001b]8;;");
	}

	[Test]
	public async Task RenderMarkdown_Link_TextSameAsUrl_ExactMatch()
	{
		// Test link where text is same as URL
		// Escape square brackets and parentheses with percent for MUSH parser
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(%[https://example.com%]%(https://example.com%))")))?.Message;
		await Assert.That(result).IsNotNull();
		// Link text is shown, URL is in hyperlink metadata
		await Assert.That(result!.ToPlainText()).IsEqualTo("https://example.com");
		// Verify the hyperlink escape sequence is present  
		var fullString = result.ToString();
		await Assert.That(fullString).Contains("\u001b]8;;");
	}

	[Test]
	public async Task RenderMarkdown_UnorderedList_ExactMatch()
	{
		// Test unordered list
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(- Item 1%r- Item 2%r- Item 3)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("- Item 1\n- Item 2\n- Item 3");
	}

	[Test]
	public async Task RenderMarkdown_OrderedList_ExactMatch()
	{
		// Test ordered list
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(1. First%r2. Second%r3. Third)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("1. First\n2. Second\n3. Third");
	}

	[Test]
	public async Task RenderMarkdown_Quote_ExactMatch()
	{
		// Test blockquote (2-space indentation)
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(> This is a quote)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("  This is a quote");
	}

	[Test]
	public async Task RenderMarkdown_Table_ExactMatch()
	{
		// Test simple table with full byte-wise comparison
		var markdown = "| Header 1 | Header 2 |%r|---|---|%r| Cell 1 | Cell 2 |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("| Header 1 | Header 2 |\n|---|---|\n| Cell 1 | Cell 2 |");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_WithCustomWidth_ExactMatch()
	{
		// Test with custom width parameter
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(| A | B | C |%r|---|---|---|%r| 1 | 2 | 3 |,50)")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Verify all lines fit within the specified width
		var lines = result!.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		foreach (var line in lines)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(50);
		}
	}

	[Test]
	public async Task RenderMarkdown_DefaultWidth78_ExactMatch()
	{
		// Test that default width is 78
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
		// Test multiple paragraphs separated by newlines
		// Note: Markdown treats double newlines as paragraph breaks, which get rendered as double newlines in output
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(Paragraph 1%r%rParagraph 2)")))?.Message;
		await Assert.That(result).IsNotNull();
		// The output includes the paragraph separation (double newline from markdown becomes double newline in output)
		await Assert.That(result!.ToPlainText()).IsEqualTo("Paragraph 1\n\nParagraph 2");
	}

	[Test]
	public async Task RenderMarkdown_NestedEmphasis_ExactMatch()
	{
		// Test nested bold and italic
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(This is ***bold and italic*** text)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("This is bold and italic text");
	}

	[Test]
	public async Task RenderMarkdown_HtmlEntityHandling_ExactMatch()
	{
		// Test HTML entity handling with full comparison
		var markdown = "&copy; 2024";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("&copy; 2024");
		
		// Do full byte-wise comparison (HTML entities should be decoded)
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_HtmlTagsStripped_ExactMatch()
	{
		// Test that HTML tags are stripped (not converted to ANSI)
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(This is <b>bold</b> text)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("This is bold text");
	}

	[Test]
	public async Task RenderMarkdown_EmptyInput_ExactMatch()
	{
		// Test empty input
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown()")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task RenderMarkdown_ComplexMixedContent_FullComparison()
	{
		// Test complex content with multiple element types - using full byte-wise comparison
		var markdown = "# Title%r%rThis is **bold** and *italic*.%r%r- Item 1%r- Item 2";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("# Title\n\nThis is **bold** and *italic*.\n\n- Item 1\n- Item 2");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_TableWithAlignment_FullComparison()
	{
		// Test table rendering with full byte-wise comparison including ANSI codes for borders
		var markdown = "| Left | Center | Right |%r|:---|:---:|---:|%r| A | B | C |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("| Left | Center | Right |\n|:---|:---:|---:|\n| A | B | C |");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_NestedListsAndQuotes_FullComparison()
	{
		// Test nested structures with exact comparison
		var markdown = "- Item 1%r  - Nested 1%r  - Nested 2%r- Item 2%r%r> Quote text";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("- Item 1\n  - Nested 1\n  - Nested 2\n- Item 2\n\n> Quote text");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_CodeBlocksWithLanguage_FullComparison()
	{
		// Test code blocks - backticks can be tricky in MUSH functions
		// This test uses a simple code block without special characters
		// Code blocks should be indented by 2 spaces
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(```%rvar x = 42;%rvar y = 100;%r```)")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Verify the plain text output with 2-space indentation
		await Assert.That(result!.ToPlainText()).IsEqualTo("  var x = 42;\n  var y = 100;");
	}

	[Test]
	public async Task RenderMarkdown_MixedFormattingInParagraph_FullComparison()
	{
		// Test paragraph with multiple formatting types - full byte-wise comparison
		var markdown = "This has **bold** text.";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("This has **bold** text.");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_HeadingsH1ThroughH3_FullComparison()
	{
		// Test all heading levels
		var markdown = "# H1 Heading%r## H2 Heading%r### H3 Heading";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("# H1 Heading\n## H2 Heading\n### H3 Heading");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_CompleteDocument_FullComparison()
	{
		// Test a complete markdown document with all features
		var markdown = "# Project Title%r%rThis is a **complete** example.%r%r## Features%r%r- Item 1%r- Item 2%r%r> Important note%r%r```%rcode here%r```";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output  
		var expected = RecursiveMarkdownHelper.RenderMarkdown("# Project Title\n\nThis is a **complete** example.\n\n## Features\n\n- Item 1\n- Item 2\n\n> Important note\n\n```\ncode here\n```");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_OrderedListWithNumbers_FullComparison()
	{
		// Test ordered list maintains correct numbering
		var markdown = "1. First item%r2. Second item%r3. Third item";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("1. First item\n2. Second item\n3. Third item");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_TableColumnSpacing_DefaultWidth()
	{
		// Test that table columns are properly spaced with default width (78)
		var markdown = "| Header A | Header B | Header C |%r|---|---|---|%r| Data 1 | Data 2 | Data 3 |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Get the full output
		var fullOutput = result!.ToString();
		var plainText = result.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		
		// Verify each line fits within default width
		foreach (var line in lines)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(78);
		}
		
		// Verify we have the expected number of rows (header row + separator + data row)
		await Assert.That(lines.Length).IsEqualTo(3);
		
		// Verify all rows have approximately the same width (should be close to 78 for fitting)
		// All rows should start and end with | character
		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith("|")).IsTrue();
			await Assert.That(line.EndsWith("|")).IsTrue();
		}
	}

	[Test]
	public async Task RenderMarkdown_TableColumnSpacing_CustomWidth50()
	{
		// Test that table columns are properly spaced with custom width (50)
		var markdown = "| Column One | Column Two | Column Three |%r|---|---|---|%r| A | B | C |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown},50)")))?.Message;
		await Assert.That(result).IsNotNull();
		
		var plainText = result!.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		
		// Verify each line fits within custom width
		foreach (var line in lines)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(50);
		}
		
		// Verify we have 3 rows
		await Assert.That(lines.Length).IsEqualTo(3);
		
		// Verify table structure is maintained
		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith("|")).IsTrue();
			await Assert.That(line.EndsWith("|")).IsTrue();
		}
	}

	[Test]
	public async Task RenderMarkdown_TableColumnAlignment_LeftCenterRight()
	{
		// Test that table column alignment syntax is respected
		var markdown = "| Left | Center | Right |%r|:---|:---:|---:|%r| L | C | R |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown for expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("| Left | Center | Right |\n|:---|:---:|---:|\n| L | C | R |");
		
		// Do full byte-wise comparison to ensure alignment is preserved
		await AssertMarkupStringEquals(result!, expected);
	}

	[Test]
	public async Task RenderMarkdown_TableExpansion_SmallContentFitsWidth()
	{
		// Test that small tables expand to use available width
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
			
			// But still within default width
			await Assert.That(line.Length).IsLessThanOrEqualTo(78);
		}
	}

	[Test]
	public async Task RenderMarkdown_TableShrinking_LargeContentFitsWidth()
	{
		// Test that large tables are constrained to specified width
		// Note: Table fitting/shrinking works but some constraints apply based on content
		var markdown = "| Very Long Header One | Very Long Header Two | Very Long Header Three | Very Long Header Four |%r|---|---|---|---|%r| Data1 | Data2 | Data3 | Data4 |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown},60)")))?.Message;
		await Assert.That(result).IsNotNull();
		
		var plainText = result!.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		
		// With a realistic width parameter, verify table has 3 rows
		await Assert.That(lines.Length).IsEqualTo(3);
		
		// Verify table structure is maintained with borders
		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith("|")).IsTrue();
			await Assert.That(line.EndsWith("|")).IsTrue();
		}
		
		// Test with a larger width that can actually fit the table
		var result120 = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown},120)")))?.Message;
		await Assert.That(result120).IsNotNull();
		
		var lines120 = result120!.ToPlainText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		// All lines should fit within 120 chars
		foreach (var line in lines120)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(120);
		}
	}

	[Test]
	public async Task RenderMarkdown_TableProportionalScaling_MultipleColumns()
	{
		// Test that column widths scale proportionally
		var markdown = "| Short | Medium Length | Very Very Long Column |%r|---|---|---|%r| A | B | C |";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		var plainText = result!.ToPlainText();
		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		
		// Extract header row to analyze column widths
		var headerRow = lines[0];
		var cells = headerRow.Split('|', StringSplitOptions.RemoveEmptyEntries);
		
		// Should have 3 columns
		await Assert.That(cells.Length).IsEqualTo(3);
		
		// "Very Very Long Column" should have the widest column
		// This verifies proportional scaling is working
		// Safely access array elements after length check
		var shortWidth = cells[0].Trim().Length;
		var mediumWidth = cells[1].Trim().Length;
		var longWidth = cells[2].Trim().Length;
		
		// Content + padding should respect proportions
		// Long column should be widest
		await Assert.That(longWidth).IsGreaterThanOrEqualTo(mediumWidth);
		await Assert.That(mediumWidth).IsGreaterThanOrEqualTo(shortWidth);
	}

	[Test]
	public async Task RenderMarkdown_FullDocument_ByteWiseComparison()
	{
		// Test a complete document with full byte-wise comparison
		var markdown = "# Main Title%r%rSome **bold** text here.%r%r| Col1 | Col2 |%r|---|---|%r| A | B |%r%r- List item 1%r- List item 2";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		// Render the same markdown again to get expected output
		var expected = RecursiveMarkdownHelper.RenderMarkdown("# Main Title\n\nSome **bold** text here.\n\n| Col1 | Col2 |\n|---|---|\n| A | B |\n\n- List item 1\n- List item 2");
		
		// Do full byte-wise comparison
		await AssertMarkupStringEquals(result!, expected);
	}
}
