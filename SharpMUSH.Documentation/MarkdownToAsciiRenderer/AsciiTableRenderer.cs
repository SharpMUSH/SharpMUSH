using System.Globalization;
using System.Text;
using Markdig.Extensions.Tables;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiTableRenderer : AsciiObjectRenderer<Table>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, Table table)
	{
		renderer.EnsureLine();
		
		// Render table rows with simple ASCII art table formatting
		bool isFirstRow = true;
		
		foreach (var rowObj in table)
		{
			var row = (TableRow)rowObj;
			
			// Start the row
			renderer.Write("| ");
			
			// Render each cell
			for (int i = 0; i < row.Count; i++)
			{
				var cellObj = row[i];
				var cell = (TableCell)cellObj;
				
				// Write cell content
				renderer.WriteChildren(cell);
				renderer.Write(" | ");
			}
			
			renderer.EnsureLine();
			
			// Add separator line after header row
			if (row.IsHeader || isFirstRow)
			{
				renderer.Write("|");
				for (int i = 0; i < row.Count; i++)
				{
					renderer.Write("---|");
				}
				renderer.EnsureLine();
			}
			
			isFirstRow = false;
		}
		
		renderer.EnsureLine();
	}
}