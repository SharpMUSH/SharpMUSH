using Markdig;
using Markdig.Syntax;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Helper class to use RecursiveMarkdownRenderer with Markdig pipeline
/// </summary>
public static class RecursiveMarkdownHelper
{
	/// <summary>
	/// Builds the standard Markdig pipeline used for all SharpMUSH help-file rendering.
	/// Extensions enabled:
	/// <list type="bullet">
	/// <item><see cref="MarkdownExtensions.UsePipeTables"/></item>
	/// <item><see cref="MarkdownExtensions.EnableTrackTrivia"/></item>
	/// <item><see cref="HelpTopicLinkExtensions.UseHelpTopicLinks"/> — converts
	///   bare <c>[topic]</c> shortcut references into ANSI OSC 8 hyperlinks with
	///   URL <c>help &lt;topic&gt;</c>.</item>
	/// <item><see cref="MarkdownExtensions.UseGenericAttributes"/> — parses the wiki's
	///   image-sizing attribute blocks (<c>{width=200}</c>) so they attach to the AST
	///   instead of leaking into terminal output as literal text. This renderer ignores
	///   the attached attributes (terminals have no image sizing).</item>
	/// <item><see cref="MarkdownExtensions.UseCustomContainers"/> — parses the wiki's
	///   <c>::: category x</c> directive blocks so the fences don't leak as text;
	///   known directives render as a dimmed placeholder.</item>
	/// <item><see cref="MarkdownExtensions.UseTaskLists"/> — renders <c>- [x]</c>
	///   task list markers.</item>
	/// <item><see cref="WikiLinkExtension"/> — parses <c>[[Page Name]]</c> wiki links;
	///   rendered as underlined display text in-game.</item>
	/// </list>
	/// </summary>
	private static MarkdownPipeline BuildPipeline() =>
		new MarkdownPipelineBuilder()
			.UsePipeTables()
			.EnableTrackTrivia() // Track HTML
			.UseHelpTopicLinks() // [topic] → help <topic> hyperlinks
			.UseGenericAttributes()
			.UseCustomContainers()
			.UseTaskLists()
			.Use<WikiLinkExtension>() // parser only; its HTML renderer hook is a no-op here
			.Build();

	/// <summary>
	/// Renders markdown text to MString using the recursive renderer
	/// </summary>
	/// <param name="markdown">The markdown text to render</param>
	/// <param name="maxWidth">Maximum width for rendered output. Tables will fit to this width with nice column spacing. Default is 78.</param>
	/// <param name="mushParser">
	/// Optional MUSH code parser used to apply syntax highlighting to
	/// <c>sharp</c> fenced code blocks.
	/// </param>
	public static MString RenderMarkdown(string markdown, int maxWidth = 78, IMUSHCodeParser? mushParser = null)
	{
		var pipeline = BuildPipeline();
		var document = Markdown.Parse(markdown, pipeline);
		var renderer = new RecursiveMarkdownRenderer(maxWidth, mushParser);
		return renderer.Render(document);
	}

	/// <summary>
	/// Renders markdown text to MString using a custom renderer
	/// </summary>
	/// <param name="markdown">The markdown text to render</param>
	/// <param name="renderer">Custom renderer instance to use</param>
	public static MString RenderMarkdown(string markdown, RecursiveMarkdownRenderer renderer)
	{
		var pipeline = BuildPipeline();
		var document = Markdown.Parse(markdown, pipeline);
		return renderer.Render(document);
	}

	/// <summary>
	/// Renders a parsed MarkdownDocument to MString using the recursive renderer
	/// </summary>
	/// <param name="document">The parsed markdown document</param>
	/// <param name="maxWidth">Maximum width for rendered output. Tables will fit to this width with nice column spacing. Default is 78.</param>
	/// <param name="mushParser">
	/// Optional MUSH code parser used to apply syntax highlighting to
	/// <c>sharp</c> fenced code blocks.
	/// </param>
	public static MString RenderDocument(MarkdownDocument document, int maxWidth = 78, IMUSHCodeParser? mushParser = null)
	{
		var renderer = new RecursiveMarkdownRenderer(maxWidth, mushParser);
		return renderer.Render(document);
	}
}
