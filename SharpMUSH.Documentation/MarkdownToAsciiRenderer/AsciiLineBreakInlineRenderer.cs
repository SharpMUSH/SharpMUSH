using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiLineBreakInlineRenderer : AsciiObjectRenderer<LineBreakInline>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, LineBreakInline obj)
	{
		throw new NotImplementedException();
	}
}