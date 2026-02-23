using Markdig.Syntax;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiListRenderer : AsciiObjectRenderer<ListBlock>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, ListBlock obj)
	{
		var listItemIndex = 1;
		// Create a dimmed style for list bullets/numbers
		var bulletStyle = Ansi.Create(faint: true);

		foreach (var item in obj)
		{
			if (item is ListItemBlock listItem)
			{
				// Determine the bullet/number prefix
				MString prefix;
				if (obj.IsOrdered)
				{
					prefix = MModule.markupSingle(bulletStyle, $"{listItemIndex}. ");
					listItemIndex++;
				}
				else
				{
					prefix = MModule.markupSingle(bulletStyle, "- ");
				}

				// Write the prefix with markup
				renderer.Write(prefix);

				// Indent subsequent lines of multi-line list items
				var indent = new string(' ', prefix.ToPlainText().Length);
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