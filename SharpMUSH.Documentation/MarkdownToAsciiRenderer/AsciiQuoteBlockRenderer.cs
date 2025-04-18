using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiQuoteBlockRenderer : AsciiObjectRenderer<QuoteBlock>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, QuoteBlock obj)
	{
		renderer.WriteLine(renderer.Render(obj).ToString()!);
	}
}