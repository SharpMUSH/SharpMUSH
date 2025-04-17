using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiParagraphRenderer : AsciiObjectRenderer<ParagraphBlock>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, ParagraphBlock obj)
	{
		renderer.WriteLine(renderer.Render(obj).ToString()!);
	}
}