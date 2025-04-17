using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiHtmlBlockRenderer : AsciiObjectRenderer<HtmlBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, HtmlBlock obj)
	{
		renderer.WriteLeafRawLines(obj, true, false);
	}
}