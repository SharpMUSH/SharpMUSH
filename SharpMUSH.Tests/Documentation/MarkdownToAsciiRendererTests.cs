using System.Drawing;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.Tables;
using MarkupString;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;
using StringExtensions = ANSILibrary.StringExtensions;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Tests for Markdown (CommonMark) to SharpMUSH Markup Text conversion.
/// </summary>
public class MarkdownToAsciiRendererTests
{
	private static MarkdownPipeline CreatePipeline()
	{
		return new MarkdownPipelineBuilder()
			.UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
			.UsePipeTables()
			.Build();
	}

	private static MString RenderMarkdown(string markdown)
	{
		var container = new MarkupStringContainer
		{
			Str = MModule.empty(),
			Inline = false
		};

		var pipeline = CreatePipeline();
		var renderer = new MarkdownToAsciiRenderer(container);
		pipeline.Setup(renderer);
		
		var doc = Markdown.Parse(markdown, pipeline);
		return renderer.RenderToMarkupString(doc);
	}

	[Test]
	public async Task RenderPlainText_ShouldWork()
	{
		// Arrange
		var markdown = "Simple plain text";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Simple plain text");
	}

	[Test]
	public async Task RenderBoldText_ShouldApplyMarkup()
	{
		// Arrange
		var markdown = "**bold text**";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("bold text");
		// Should contain ANSI codes for bold
		await Assert.That(result.ToString()).Contains(ANSILibrary.ANSI.Bold);
	}

	[Test]
	public async Task RenderItalicText_ShouldApplyMarkup()
	{
		// Arrange
		var markdown = "*italic text*";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("italic text");
		// Should contain ANSI codes
		await Assert.That(result.ToString()).Contains(ANSILibrary.ANSI.Bold);
	}

	[Test]
	public async Task RenderHeading1_ShouldApplyMarkup()
	{
		// Arrange
		var markdown = "# Heading 1";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 1\n");
		// Should contain ANSI codes for underline and bold
		await Assert.That(result.ToString()).Contains(ANSILibrary.ANSI.Underlined);
		await Assert.That(result.ToString()).Contains(ANSILibrary.ANSI.Bold);
	}

	[Test]
	public async Task RenderHeading2_ShouldApplyMarkup()
	{
		// Arrange
		var markdown = "## Heading 2";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 2\n");
		await Assert.That(result.ToString()).Contains(ANSILibrary.ANSI.Underlined);
		await Assert.That(result.ToString()).Contains(ANSILibrary.ANSI.Bold);
	}

	[Test]
	public async Task RenderHeading3_ShouldApplyMarkup()
	{
		// Arrange
		var markdown = "### Heading 3";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Heading 3\n");
		await Assert.That(result.ToString()).Contains(ANSILibrary.ANSI.Underlined);
	}

	[Test]
	public async Task RenderInlineCode_ShouldWork()
	{
		// Arrange
		var markdown = "Some `inline code` here";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("Some inline code here");
	}

	[Test]
	public async Task RenderCodeBlock_ShouldWork()
	{
		// Arrange
		var markdown = "```\ncode line 1\ncode line 2\n```";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("code line 1");
		await Assert.That(result.ToPlainText()).Contains("code line 2");
	}

	[Test]
	public async Task RenderLink_ShouldWork()
	{
		// Arrange
		var markdown = "[link text](https://example.com)";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("link text");
		// Note: Link URL storage in markup is a TODO - for now we just render the text
	}

	[Test]
	public async Task RenderAutolink_ShouldWork()
	{
		// Arrange
		var markdown = "<https://example.com>";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("https://example.com");
	}

	[Test]
	public async Task RenderLineBreak_ShouldWork()
	{
		// Arrange - two spaces at end of line creates line break
		var markdown = "Line 1  \nLine 2";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Line 1");
		await Assert.That(result.ToPlainText()).Contains("Line 2");
	}

	[Test]
	public async Task RenderMultipleParagraphs_ShouldSeparate()
	{
		// Arrange
		var markdown = "Paragraph 1\n\nParagraph 2";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Paragraph 1");
		await Assert.That(result.ToPlainText()).Contains("Paragraph 2");
	}

	[Test]
	public async Task RenderQuote_ShouldIndent()
	{
		// Arrange
		var markdown = "> This is a quote";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("This is a quote");
		// Should be indented
		await Assert.That(result.ToString()).Contains("  ");
	}

	[Test]
	public async Task RenderUnorderedList_ShouldWork()
	{
		// Arrange
		var markdown = "- Item 1\n- Item 2\n- Item 3";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Item 1");
		await Assert.That(result.ToPlainText()).Contains("Item 2");
		await Assert.That(result.ToPlainText()).Contains("Item 3");
	}

	[Test]
	public async Task RenderOrderedList_ShouldWork()
	{
		// Arrange
		var markdown = "1. Item 1\n2. Item 2\n3. Item 3";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Item 1");
		await Assert.That(result.ToPlainText()).Contains("Item 2");
		await Assert.That(result.ToPlainText()).Contains("Item 3");
	}

	[Test]
	public async Task RenderThematicBreak_ShouldWork()
	{
		// Arrange
		var markdown = "Text before\n\n---\n\nText after";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Text before");
		await Assert.That(result.ToPlainText()).Contains("Text after");
		await Assert.That(result.ToPlainText()).Contains("---");
	}

	[Test]
	public async Task RenderTable_ShouldWork()
	{
		// Arrange
		var markdown = @"| Header 1 | Header 2 |
| --- | --- |
| Cell 1 | Cell 2 |
| Cell 3 | Cell 4 |";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Header 1");
		await Assert.That(result.ToPlainText()).Contains("Header 2");
		await Assert.That(result.ToPlainText()).Contains("Cell 1");
		await Assert.That(result.ToPlainText()).Contains("Cell 2");
	}

	[Test]
	public async Task RenderMixedContent_ShouldWork()
	{
		// Arrange
		var markdown = "# Header\n\nSome **bold** and *italic* text.\n\n- List item 1\n- List item 2";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Header");
		await Assert.That(result.ToPlainText()).Contains("bold");
		await Assert.That(result.ToPlainText()).Contains("italic");
		await Assert.That(result.ToPlainText()).Contains("List item 1");
		await Assert.That(result.ToPlainText()).Contains("List item 2");
	}

	[Test]
	public async Task RenderNestedEmphasis_ShouldWork()
	{
		// Arrange
		var markdown = "***bold and italic***";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).IsEqualTo("bold and italic");
	}

	[Test]
	public async Task RenderHtmlEntity_ShouldWork()
	{
		// Arrange
		var markdown = "Test &amp; entity";
		
		// Act
		var result = RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("&");
	}

	[Test]
	public async Task ExistingTest_MarkdownToMarkup_ShouldStillPass()
	{
		// This is the existing test from HelpfileTests
		var container = new MarkupStringContainer
		{
			Str = MModule.empty(),
			Inline = false
		};

		var pipeline = new MarkdownPipelineBuilder().UseAutoIdentifiers(AutoIdentifierOptions.GitHub).Build();
		var renderer = new MarkdownToAsciiRenderer(container);
		pipeline.Setup(renderer);
		
		var markdown = "# Header1 *Bolded*\nNewline?";
		var headerStyle = MarkupImplementation.AnsiMarkup.Create(underlined: true, bold: true);
		var boldStyle = MarkupImplementation.AnsiMarkup.Create(bold: true, foreground: StringExtensions.rgb(Color.White));
		
		var header = MModule.markupMultiple(headerStyle, 
			[
				MModule.single("Header1 "), 
				MModule.markupMultiple(boldStyle, 
					[
						MModule.single("Bolded")
					])
			]);
		var body = MModule.single("Newline?");
		var expectedResult = MModule.multipleWithDelimiter(MModule.single("\n"), [header, body]);
		var doc = Markdown.Parse(markdown, pipeline);
		var finalResult = renderer.RenderToMarkupString(doc);
		
		Console.WriteLine("Expected: " + expectedResult.ToString());
		Console.WriteLine("Actual: " + finalResult.ToString());
		
		await Assert.That(finalResult.ToString()).IsEqualTo(expectedResult.ToString());
	}
}
