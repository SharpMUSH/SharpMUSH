using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiThematicBreakRenderer : AsciiObjectRenderer<ThematicBreakBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, ThematicBreakBlock obj)
	{
		renderer.WriteLine(obj.Content.Text);
	}
}