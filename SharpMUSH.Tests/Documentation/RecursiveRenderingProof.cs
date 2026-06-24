using SharpMUSH.Documentation.MarkdownToAsciiRenderer;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// Demonstration that proves Markdig supports recursive rendering.
/// This test shows that we can traverse Markdig's AST recursively,
/// with each method returning MString instead of writing to a buffer.
/// </summary>
public class RecursiveRenderingProof
{
	private const string Faint = "\u001b[2m";
	private const string Bold = "\u001b[1m";
	private const string Clear = "\u001b[0m";
	private static string Foreground(byte r, byte g, byte b) => $"\u001b[38;2;{r};{g};{b}m";

	[Test]
	public async Task ProofOfConcept_RecursiveRendering_Works()
	{
		var markdown = @"# Heading

This is **bold** and *italic* text.

- List item 1
- List item 2

| Column 1 | Column 2 |
| :--- | ---: |
| Left aligned | Right aligned |
| Short | Long content here |";

		var result = RecursiveMarkdownHelper.RenderMarkdown(markdown);

		var plainText = result.ToPlainText();

		await Assert.That(plainText.Contains("Heading")).IsTrue();
		await Assert.That(plainText.Contains("bold")).IsTrue();
		await Assert.That(plainText.Contains("italic")).IsTrue();
		await Assert.That(plainText.Contains("List item 1")).IsTrue();
		await Assert.That(plainText.Contains("List item 2")).IsTrue();
		await Assert.That(plainText.Contains("Column 1")).IsTrue();
		await Assert.That(plainText.Contains("Column 2")).IsTrue();
		await Assert.That(plainText.Contains("Left aligned")).IsTrue();
		await Assert.That(plainText.Contains("Right aligned")).IsTrue();

		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Faint) || fullString.Contains(Bold)).IsTrue();

		await Assert.That(plainText.Contains("|")).IsTrue();
	}

	[Test]
	public async Task ProofOfConcept_TableColumnAlignment_UsesTextAlignerModule()
	{
		var markdown = @"| Short | Medium Text | Very Long Content Here |
| --- | --- | --- |
| A | B | C |";

		var result = RecursiveMarkdownHelper.RenderMarkdown(markdown);
		var plainText = result.ToPlainText();

		await Assert.That(plainText.Contains("Short")).IsTrue();
		await Assert.That(plainText.Contains("Medium Text")).IsTrue();
		await Assert.That(plainText.Contains("Very Long Content Here")).IsTrue();
		await Assert.That(plainText.Contains("|")).IsTrue();

		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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

		var result = RecursiveMarkdownHelper.RenderMarkdown(markdown);

		await Assert.That(result.ToPlainText()).IsEqualTo("Simple text");

		var fullString = result.ToString();
		await Assert.That(fullString.Contains(Foreground(255, 255, 255))).IsTrue();
		await Assert.That(fullString.Contains(Bold)).IsTrue();

		await Assert.That(result.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task ProofOfConcept_MaxWidth_ConstrainsTableColumns()
	{
		var markdown = @"| Column1 | Column2 | Column3 |
| --- | --- | --- |
| Some data | More data | Even more |";

		var result = RecursiveMarkdownHelper.RenderMarkdown(markdown, maxWidth: 60);
		var plainText = result.ToPlainText();

		var lines = plainText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		foreach (var line in lines)
		{
			await Assert.That(line.Length).IsLessThanOrEqualTo(60);
		}

		await Assert.That(plainText.Contains("Column1")).IsTrue();
		await Assert.That(plainText.Contains("Some data")).IsTrue();
	}
}
