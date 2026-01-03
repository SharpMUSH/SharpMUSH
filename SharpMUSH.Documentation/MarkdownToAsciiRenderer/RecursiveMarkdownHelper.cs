using Markdig;
using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Helper class to use RecursiveMarkdownRenderer with Markdig pipeline
/// </summary>
public static class RecursiveMarkdownHelper
{
	/// <summary>
	/// Renders markdown text to MString using the recursive renderer
	/// </summary>
	public static MString RenderMarkdown(string markdown)
	{
		var pipeline = new MarkdownPipelineBuilder()
			.UsePipeTables()
			.Build();
		
		var document = Markdown.Parse(markdown, pipeline);
		var renderer = new RecursiveMarkdownRenderer();
		return renderer.Render(document);
	}
	
	/// <summary>
	/// Renders a parsed MarkdownDocument to MString using the recursive renderer
	/// </summary>
	public static MString RenderDocument(MarkdownDocument document)
	{
		var renderer = new RecursiveMarkdownRenderer();
		return renderer.Render(document);
	}
}
