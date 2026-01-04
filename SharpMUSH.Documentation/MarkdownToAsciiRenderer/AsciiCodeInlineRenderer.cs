using Markdig.Syntax.Inlines;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiCodeInlineRenderer : AsciiObjectRenderer<CodeInline>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, CodeInline obj)
	{
		renderer.Write(obj.Content);
	}
}