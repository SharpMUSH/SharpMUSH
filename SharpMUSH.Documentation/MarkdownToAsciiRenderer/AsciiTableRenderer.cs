using System.Globalization;
using Markdig.Extensions.Tables;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public class AsciiTableRenderer : AsciiObjectRenderer<Table>
{
	protected override void Write(MarkdownToAsciiRenderer renderer, Table table)
	{
		renderer.EnsureLine();
		renderer.WriteLine("<table>");

		bool hasBody = false;
		bool hasAlreadyHeader = false;
		bool isHeaderOpen = false;
		bool hasColumnWidth = false;
		
		// TODO: This is copied and adjusted from the HTML code. 
		// SharpMUSH likely does not care about Column Width here. And instead wants to check the width of all elements.
		// or cheat and assume the user always gives the right amount.
		foreach (var tableColumnDefinition in table.ColumnDefinitions)
		{
			if (tableColumnDefinition.Width != 0.0f && Math.Abs(tableColumnDefinition.Width - 1.0f) > 0.001f)
			{
				hasColumnWidth = true;
				break;
			}
		}

		if (hasColumnWidth)
		{
			foreach (var tableColumnDefinition in table.ColumnDefinitions)
			{
				var width = Math.Round(tableColumnDefinition.Width * 100) / 100;
				var widthValue = string.Format(CultureInfo.InvariantCulture, "{0:0.##}", width);
				renderer.WriteLine($"<col style=\"width:{widthValue}%\" />");
			}
		}

		foreach (var rowObj in table)
		{
			var row = (TableRow)rowObj;
			if (row.IsHeader)
			{
				// Allow a single thead
				if (!hasAlreadyHeader)
				{
					renderer.WriteLine("<thead>");
					isHeaderOpen = true;
				}

				hasAlreadyHeader = true;
			}
			else if (!hasBody)
			{
				if (isHeaderOpen)
				{
					renderer.WriteLine("</thead>");
					isHeaderOpen = false;
				}

				renderer.WriteLine("<tbody>");
				hasBody = true;
			}

			renderer.WriteLine("<tr>");
			for (int i = 0; i < row.Count; i++)
			{
				var cellObj = row[i];
				var cell = (TableCell)cellObj;

				renderer.EnsureLine();
				renderer.Write(row.IsHeader ? "<th" : "<td");
				if (cell.ColumnSpan != 1)
				{
					renderer.Write($" colspan=\"{cell.ColumnSpan}\"");
				}

				if (cell.RowSpan != 1)
				{
					renderer.Write($" rowspan=\"{cell.RowSpan}\"");
				}

				if (table.ColumnDefinitions.Count > 0)
				{
					var columnIndex = cell.ColumnIndex < 0 || cell.ColumnIndex >= table.ColumnDefinitions.Count
						? i
						: cell.ColumnIndex;
					columnIndex = columnIndex >= table.ColumnDefinitions.Count ? table.ColumnDefinitions.Count - 1 : columnIndex;
					var alignment = table.ColumnDefinitions[columnIndex].Alignment;
					if (alignment.HasValue)
					{
						switch (alignment)
						{
							case TableColumnAlign.Center:
								renderer.Write(" style=\"text-align: center;\"");
								break;
							case TableColumnAlign.Right:
								renderer.Write(" style=\"text-align: right;\"");
								break;
							case TableColumnAlign.Left:
								renderer.Write(" style=\"text-align: left;\"");
								break;
						}
					}
				}

				renderer.Write(">");

				renderer.Write(cell);
				renderer.WriteLine(row.IsHeader ? "</th>" : "</td>");
			}

			renderer.WriteLine("</tr>");
		}

		if (hasBody)
		{
			renderer.WriteLine("</tbody>");
		}
		else if (isHeaderOpen)
		{
			renderer.WriteLine("</thead>");
		}

		renderer.WriteLine("</table>");
	}
}