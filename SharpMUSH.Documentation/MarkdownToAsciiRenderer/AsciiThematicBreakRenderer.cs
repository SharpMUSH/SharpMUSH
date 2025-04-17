using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiThematicBreakRenderer : AsciiObjectRenderer<ThematicBreakBlock>
{
	protected override void Write(Documentation.MarkdownToAsciiRenderer.MarkdownToAsciiRenderer renderer, ThematicBreakBlock obj)
	{
		renderer.WriteLine(renderer.Render(obj).ToString()!);
	}
}