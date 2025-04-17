using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiEmphasisInlineRenderer : AsciiObjectRenderer<EmphasisInline>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, EmphasisInline obj)
	{
		throw new NotImplementedException();
	}
}