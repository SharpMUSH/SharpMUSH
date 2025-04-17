using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiHtmlEntityInlineRenderer : AsciiObjectRenderer<HtmlEntityInline>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, HtmlEntityInline obj)
	{
		throw new NotImplementedException();
	}
}