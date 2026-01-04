using System.Drawing;
using System.Globalization;
using System.Text;
using ANSILibrary;
using Markdig.Extensions.Tables;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiTableRenderer : AsciiObjectRenderer<Table>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, Table table)
	{
		renderer.EnsureLine();
		
		// Create a dimmed style for table borders
		var borderStyle = Ansi.Create(faint: true);
		
		// Render table rows with simple ASCII art table formatting
		bool isFirstRow = true;
		
		foreach (var rowObj in table)
		{
			var row = (TableRow)rowObj;
			
			// Start the row with styled border
			renderer.Write(MModule.markupSingle(borderStyle, "| "));
			
			// Render each cell
			for (int i = 0; i < row.Count; i++)
			{
				var cellObj = row[i];
				var cell = (TableCell)cellObj;
				
				// Write cell content
				renderer.WriteChildren(cell);
				
				// Write cell separator with styled border
				renderer.Write(MModule.markupSingle(borderStyle, " | "));
			}
			
			renderer.EnsureLine();
			
			// Add separator line after header row
			if (row.IsHeader || isFirstRow)
			{
				var separatorBuilder = new StringBuilder();
				separatorBuilder.Append("|");
				for (int i = 0; i < row.Count; i++)
				{
					separatorBuilder.Append("---|");
				}
				renderer.Write(MModule.markupSingle(borderStyle, separatorBuilder.ToString()));
				renderer.EnsureLine();
			}
			
			isFirstRow = false;
		}
		
		renderer.EnsureLine();
	}
}