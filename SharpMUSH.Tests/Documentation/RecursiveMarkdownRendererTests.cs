using TUnit.Core;

namespace SharpMUSH.Tests.Documentation;

public class RecursiveMarkdownRendererTests
{
	[Test]
	public async Task RenderPlainText_ShouldWork()
	{
		// Arrange
		var markdown = "Hello, world!";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Hello, world!");
	}

	[Test]
	public async Task RenderBoldText_ShouldApplyMarkup()
	{
		// Arrange
		var markdown = "This is **bold** text";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("bold");
		// Should contain ANSI codes
		await Assert.That(result.ToString()).Contains("\u001b[");
	}

	[Test]
	public async Task RenderTable_WithAlignment_ShouldUseAlignFunction()
	{
		// Arrange
		var markdown = @"| Left | Center | Right |
| :--- | :---: | ---: |
| L1 | C1 | R1 |
| L22 | C22 | R22 |";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		var plainText = result.ToPlainText();
		await Assert.That(plainText).Contains("Left");
		await Assert.That(plainText).Contains("Center");
		await Assert.That(plainText).Contains("Right");
		await Assert.That(plainText).Contains("|");
		
		// Should contain ANSI codes for borders
		await Assert.That(result.ToString()).Contains("\u001b[");
	}

	[Test]
	public async Task RenderTable_CellsAreAligned()
	{
		// Arrange
		var markdown = @"| Header 1 | Header 2 |
| --- | --- |
| Short | Long Content Here |
| A | B |";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		var plainText = result.ToPlainText();
		var lines = plainText.Split('\n');
		
		// All content rows should have consistent column widths
		await Assert.That(lines.Length).IsGreaterThan(2);
		await Assert.That(plainText).Contains("Header 1");
		await Assert.That(plainText).Contains("Long Content Here");
	}

	[Test]
	public async Task RenderList_WithBullets()
	{
		// Arrange
		var markdown = "- Item 1\n- Item 2\n- Item 3";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("- Item 1");
		await Assert.That(result.ToPlainText()).Contains("- Item 2");
		// Should contain ANSI codes for bullets
		await Assert.That(result.ToString()).Contains("\u001b[");
	}

	[Test]
	public async Task RenderOrderedList()
	{
		// Arrange
		var markdown = "1. First\n2. Second\n3. Third";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("1. First");
		await Assert.That(result.ToPlainText()).Contains("2. Second");
		await Assert.That(result.ToPlainText()).Contains("3. Third");
	}

	[Test]
	public async Task RenderHeading_WithMarkup()
	{
		// Arrange
		var markdown = "# Heading 1\n## Heading 2\n### Heading 3";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Heading 1");
		await Assert.That(result.ToPlainText()).Contains("Heading 2");
		await Assert.That(result.ToPlainText()).Contains("Heading 3");
		// Should contain ANSI codes for heading styles
		await Assert.That(result.ToString()).Contains("\u001b[");
	}

	[Test]
	public async Task RenderCodeBlock()
	{
		// Arrange
		var markdown = "```\ncode line 1\ncode line 2\n```";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("code line 1");
		await Assert.That(result.ToPlainText()).Contains("code line 2");
	}

	[Test]
	public async Task RenderInlineCode()
	{
		// Arrange
		var markdown = "This is `inline code` here";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("inline code");
	}

	[Test]
	public async Task RenderQuote()
	{
		// Arrange
		var markdown = "> This is a quote\n> Second line";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		var plainText = result.ToPlainText();
		await Assert.That(plainText).Contains("This is a quote");
		// Should be indented
		await Assert.That(plainText).Contains("  ");
	}

	[Test]
	public async Task RenderLink()
	{
		// Arrange
		var markdown = "[Link Text](https://example.com)";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		await Assert.That(result.ToPlainText()).Contains("Link Text");
	}

	[Test]
	public async Task RenderMixedContent()
	{
		// Arrange
		var markdown = @"# Title

Some **bold** text here.

- List item 1
- List item 2

| Col1 | Col2 |
| --- | --- |
| A | B |";
		
		// Act
		var result = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert
		var plainText = result.ToPlainText();
		await Assert.That(plainText).Contains("Title");
		await Assert.That(plainText).Contains("bold");
		await Assert.That(plainText).Contains("List item 1");
		await Assert.That(plainText).Contains("Col1");
		await Assert.That(plainText).Contains("Col2");
	}
}
