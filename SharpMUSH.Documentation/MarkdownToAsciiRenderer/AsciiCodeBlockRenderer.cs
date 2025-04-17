using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiCodeBlockRenderer : AsciiObjectRenderer<CodeBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, CodeBlock obj)
	{
		renderer.WriteLeafRawLines(obj, true, false);
	}
}