using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiHtmlInlineRenderer : AsciiObjectRenderer<HtmlInline>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, HtmlInline obj)
	{
		throw new NotImplementedException();
	}
}