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
	/// <param name="markdown">The markdown text to render</param>
	/// <param name="maxWidth">Maximum width for rendered output. Used to constrain table column widths. Default is 80.</param>
	public static MString RenderMarkdown(string markdown, int maxWidth = 80)
	{
		var pipeline = new MarkdownPipelineBuilder()
			.UsePipeTables()
			.Build();
		
		var document = Markdown.Parse(markdown, pipeline);
		var renderer = new RecursiveMarkdownRenderer(maxWidth);
		return renderer.Render(document);
	}
	
	/// <summary>
	/// Renders a parsed MarkdownDocument to MString using the recursive renderer
	/// </summary>
	/// <param name="document">The parsed markdown document</param>
	/// <param name="maxWidth">Maximum width for rendered output. Used to constrain table column widths. Default is 80.</param>
	public static MString RenderDocument(MarkdownDocument document, int maxWidth = 80)
	{
		var renderer = new RecursiveMarkdownRenderer(maxWidth);
		return renderer.Render(document);
	}
}
