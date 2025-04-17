using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiLinkInlineRenderer : AsciiObjectRenderer<LinkInline>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, LinkInline obj)
	{
		throw new NotImplementedException();
	}
}