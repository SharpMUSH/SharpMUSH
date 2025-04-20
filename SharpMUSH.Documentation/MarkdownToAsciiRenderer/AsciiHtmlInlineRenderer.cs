using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiHtmlInlineRenderer : AsciiObjectRenderer<HtmlInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, HtmlInline obj)
	{
		renderer.WriteRaw(obj.Tag);
	}
}