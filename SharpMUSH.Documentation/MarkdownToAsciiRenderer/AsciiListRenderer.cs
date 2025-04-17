using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiListRenderer : AsciiObjectRenderer<ListBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, ListBlock obj)
	{
		foreach (var item in obj)
		{
			// TODO: Check this.
			renderer.WriteLine(renderer.Render(item).ToString()!);
		}
	}
}