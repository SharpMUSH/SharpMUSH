using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiListRenderer : AsciiObjectRenderer<ListBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, ListBlock obj)
	{
		var listItemIndex = 1;
		
		foreach (var item in obj)
		{
			if (item is ListItemBlock listItem)
			{
				// Determine the bullet/number prefix
				string prefix;
				if (obj.IsOrdered)
				{
					prefix = $"{listItemIndex}. ";
					listItemIndex++;
				}
				else
				{
					prefix = "- ";
				}
				
				// Write the prefix
				renderer.Write(prefix);
				
				// Indent subsequent lines of multi-line list items
				var indent = new string(' ', prefix.Length);
				renderer.PushIndent(indent);
				
				// Write the list item content
				renderer.WriteChildren(listItem);
				
				// Pop the indent
				renderer.PopIndent();
				
				// Ensure we're on a new line for the next item
				renderer.EnsureLine();
			}
		}
	}
}