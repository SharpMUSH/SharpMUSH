using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiQuoteBlockRenderer : AsciiObjectRenderer<QuoteBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, QuoteBlock obj)
	{
		renderer.PushIndent("  ");
		renderer.WriteChildren(obj);
		renderer.PopIndent();
	}
}