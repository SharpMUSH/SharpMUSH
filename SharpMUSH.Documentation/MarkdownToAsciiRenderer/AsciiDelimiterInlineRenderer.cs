using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiDelimiterInlineRenderer : AsciiObjectRenderer<DelimiterInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, DelimiterInline obj)
	{
		renderer.Write(obj);
	}
}