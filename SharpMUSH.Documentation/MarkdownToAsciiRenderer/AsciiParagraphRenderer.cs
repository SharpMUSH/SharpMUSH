using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiParagraphRenderer : AsciiObjectRenderer<ParagraphBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, ParagraphBlock obj)
	{
		renderer.WriteLeafInline(obj);
	}
}