using Markdig;
using Markdig.Parsers.Inlines;
using Markdig.Renderers;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

/// <summary>
/// Markdig extension that enables bare <c>[topic]</c> shortcut reference link
/// recognition in help-file markdown.
/// <para>
/// Register by calling <see cref="HelpTopicLinkExtensions.UseHelpTopicLinks"/> on a
/// <see cref="MarkdownPipelineBuilder"/>:
/// </para>
/// <code>
/// var pipeline = new MarkdownPipelineBuilder()
///     .UsePipeTables()
///     .UseHelpTopicLinks()
///     .Build();
/// </code>
/// <para>
/// The extension adds a <see cref="HelpTopicInlineParser"/> immediately before the
/// built-in <see cref="LinkInlineParser"/> so it receives first opportunity to match
/// bare <c>[topic]</c> patterns.  The resulting <see cref="Markdig.Syntax.Inlines.LinkInline"/>
/// nodes carry a <c>help &lt;topic&gt;</c> URL which is rendered as an ANSI OSC 8
/// hyperlink by the existing link renderer.
/// </para>
/// </summary>
public sealed class HelpTopicLinkExtension : IMarkdownExtension
{
	/// <inheritdoc/>
	public void Setup(MarkdownPipelineBuilder pipeline)
	{
		// Insert before LinkInlineParser so our parser gets first shot at '['.
		// AddIfNotAlready / Contains guard against duplicate registration if the
		// pipeline is built more than once.
		if (!pipeline.InlineParsers.Contains<HelpTopicInlineParser>())
			pipeline.InlineParsers.InsertBefore<LinkInlineParser>(new HelpTopicInlineParser());
	}

	/// <inheritdoc/>
	public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
	{
		// No renderer registration required — the existing RenderLink / AsciiLinkInlineRenderer
		// already handles LinkInline nodes with any URL, including "help <topic>".
	}
}

/// <summary>
/// <see cref="MarkdownPipelineBuilder"/> extension method for <see cref="HelpTopicLinkExtension"/>.
/// </summary>
public static class HelpTopicLinkExtensions
{
	/// <summary>
	/// Enables recognition of bare <c>[topic]</c> shortcut reference links and converts
	/// them to ANSI OSC 8 hyperlinks with URL <c>help &lt;topic&gt;</c>.
	/// </summary>
	public static MarkdownPipelineBuilder UseHelpTopicLinks(this MarkdownPipelineBuilder pipeline)
	{
		pipeline.Extensions.AddIfNotAlready<HelpTopicLinkExtension>();
		return pipeline;
	}
}
