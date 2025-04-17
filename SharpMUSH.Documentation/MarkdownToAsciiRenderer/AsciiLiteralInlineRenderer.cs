using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiLiteralInlineRenderer : AsciiObjectRenderer<LiteralInline>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, LiteralInline obj)
	{
		renderer.Write(obj);
	}
}