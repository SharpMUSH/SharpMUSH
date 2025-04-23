using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiListRenderer : AsciiObjectRenderer<ListBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, ListBlock obj)
	{
		// TODO: Create a Queue on the Renderer for list items.
		renderer.WriteChildren(obj);
		// Pop the Queue.
	}
}