using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiListItemRenderer : AsciiObjectRenderer<ListItemBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, ListItemBlock obj)
	{
		// List items are handled by the ListRenderer which manages numbering/bullets
		// Here we just write the content of the list item
		renderer.WriteChildren(obj);
	}
}
