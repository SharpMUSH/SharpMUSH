using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiLineBreakInlineRenderer : AsciiObjectRenderer<LineBreakInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, LineBreakInline obj)
	{
		renderer.WriteLine(Environment.NewLine);
	}
}