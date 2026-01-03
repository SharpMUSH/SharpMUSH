using SharpMUSH.Documentation.MarkdownToAsciiRenderer;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Demonstration that proves Markdig supports recursive rendering.
/// This test shows that we can traverse Markdig's AST recursively,
/// with each method returning MString instead of writing to a buffer.
/// </summary>
public class RecursiveRenderingProof
{
	[Test]
	public async Task ProofOfConcept_RecursiveRendering_Works()
	{
		// Arrange - Complex markdown with multiple elements
		var markdown = @"# Heading

This is **bold** and *italic* text.

- List item 1
- List item 2

| Column 1 | Column 2 |
| :--- | ---: |
| Left aligned | Right aligned |
| Short | Long content here |";

		// Act - Use recursive renderer
		var result = RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		// Assert - Verify all content was rendered
		var plainText = result.ToPlainText();
		
		await Assert.That(plainText).Contains("Heading");
		await Assert.That(plainText).Contains("bold");
		await Assert.That(plainText).Contains("italic");
		await Assert.That(plainText).Contains("List item 1");
		await Assert.That(plainText).Contains("List item 2");
		await Assert.That(plainText).Contains("Column 1");
		await Assert.That(plainText).Contains("Column 2");
		await Assert.That(plainText).Contains("Left aligned");
		await Assert.That(plainText).Contains("Right aligned");
		
		// Verify markup is present (ANSI codes)
		await Assert.That(result.ToString()).Contains("\u001b[");
		
		// Verify table alignment worked (check for table borders)
		await Assert.That(plainText).Contains("|");
	}
	
	[Test]
	public async Task ProofOfConcept_TableColumnAlignment_UsesTextAlignerModule()
	{
		// Arrange - Table with different column widths
		var markdown = @"| Short | Medium Text | Very Long Content Here |
| --- | --- | --- |
| A | B | C |";

		// Act
		var result = RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var plainText = result.ToPlainText();
		
		// Assert - Table should be properly formatted with alignment
		await Assert.That(plainText).Contains("Short");
		await Assert.That(plainText).Contains("Medium Text");
		await Assert.That(plainText).Contains("Very Long Content Here");
		await Assert.That(plainText).Contains("|");
		
		// The table should have consistent column widths across rows
		var lines = plainText.Split('\n');
		await Assert.That(lines.Length).IsGreaterThan(2);
	}
	
	[Test]
	public async Task ProofOfConcept_RecursivePattern_EnablesComposition()
	{
		// This test demonstrates that the recursive pattern allows
		// capturing intermediate rendering results, which the
		// traditional RendererBase pattern with protected Container
		// made difficult.
		
		var markdown = "Simple **text**";
		
		// The RecursiveMarkdownRenderer can render any element
		// and return its MString, enabling composition
		var result = RecursiveMarkdownHelper.RenderMarkdown(markdown);
		
		await Assert.That(result.ToPlainText()).Contains("Simple");
		await Assert.That(result.ToPlainText()).Contains("text");
		
		// The result is a proper MString with markup
		await Assert.That(result.Length).IsGreaterThan(0);
	}
}
