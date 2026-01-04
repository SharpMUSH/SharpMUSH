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
		// Test bold text with exact plain text match and ANSI code verification
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(This is **bold** text)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("This is bold text");
		
		// Verify ANSI codes are present in the formatted output
		var formatted = result.ToString();
		await Assert.That(formatted.Contains(Bold)).IsTrue();
		await Assert.That(formatted.Contains(Foreground(255, 255, 255))).IsTrue();
	}

	[Test]
	public async Task RenderMarkdown_ItalicText_ExactMatch()
	{
		// Test italic text (rendered as bold in this implementation)
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(This is *italic* text)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("This is italic text");
		
		// Verify ANSI codes are present
		var formatted = result.ToString();
		await Assert.That(formatted.Contains(Bold)).IsTrue();
	}

	[Test]
	public async Task RenderMarkdown_Heading1_ExactMatch()
	{
		// Test H1 heading with underline and bold
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(# Heading 1)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("Heading 1");
		
		// Verify ANSI codes for underline and bold
		var formatted = result.ToString();
		await Assert.That(formatted.Contains(Underlined)).IsTrue();
		await Assert.That(formatted.Contains(Bold)).IsTrue();
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
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(```%rcode line 1%rcode line 2%r```)")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("code line 1\ncode line 2");
	}

	[Test]
	public async Task RenderMarkdown_Link_ExactMatch()
	{
		// Test link - should extract just the link text
		// Note: Complex URLs in MUSH function syntax can be tricky due to special characters
		// This test verifies that link text is properly extracted
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown([Click here](url))")))?.Message;
		await Assert.That(result).IsNotNull();
		await Assert.That(result!.ToPlainText()).IsEqualTo("Click here");
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
		// Test simple table - verify it contains expected content
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(| Header 1 | Header 2 |%r|---|---|%r| Cell 1 | Cell 2 |)")))?.Message;
		await Assert.That(result).IsNotNull();
		
		var plainText = result!.ToPlainText();
		await Assert.That(plainText.Contains("Header 1")).IsTrue();
		await Assert.That(plainText.Contains("Header 2")).IsTrue();
		await Assert.That(plainText.Contains("Cell 1")).IsTrue();
		await Assert.That(plainText.Contains("Cell 2")).IsTrue();
		await Assert.That(plainText.Contains("|")).IsTrue(); // Contains table borders
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
		// Test HTML entity handling
		var result = (await Parser.FunctionParse(MModule.single("rendermarkdown(&copy; 2024)")))?.Message;
		await Assert.That(result).IsNotNull();
		// HTML entities should be decoded
		await Assert.That(result!.ToPlainText().Contains("©")).IsTrue();
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
	public async Task RenderMarkdown_ComplexMixedContent_ExactMatch()
	{
		// Test complex content with multiple element types
		var markdown = "# Title%r%rThis is **bold** and *italic*.%r%r- Item 1%r- Item 2";
		var result = (await Parser.FunctionParse(MModule.single($"rendermarkdown({markdown})")))?.Message;
		await Assert.That(result).IsNotNull();
		
		var plainText = result!.ToPlainText();
		await Assert.That(plainText.Contains("Title")).IsTrue();
		await Assert.That(plainText.Contains("bold")).IsTrue();
		await Assert.That(plainText.Contains("italic")).IsTrue();
		await Assert.That(plainText.Contains("- Item 1")).IsTrue();
		await Assert.That(plainText.Contains("- Item 2")).IsTrue();
	}
}
