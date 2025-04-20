using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiHtmlEntityInlineRenderer : AsciiObjectRenderer<HtmlEntityInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, HtmlEntityInline obj)
	{
		renderer.WriteRaw(obj.Original.Text);
	}
}