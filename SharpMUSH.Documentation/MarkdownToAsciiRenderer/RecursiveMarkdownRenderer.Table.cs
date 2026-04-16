using Markdig.Extensions.Tables;
using SharpMUSH.MarkupString;
using SharpMUSH.MarkupString.TextAlignerModule;
using System.Text;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	protected virtual MString RenderTable(Table table)
	{
		var borderStyle = _dimStyle;

		var allRows = table
			.OfType<TableRow>()
			.Select(row => (
				IsHeader: row.IsHeader,
				Cells: row.OfType<TableCell>()
					.Select(cell => RenderTableCell(cell))
					.ToList()
			))
			.ToList();

		if (allRows.Count == 0) return MModule.empty();

		// Calculate column widths
		var columnCount = allRows.Max(r => r.Cells.Count);
		var columnWidths = new int[columnCount];

		for (var col = 0; col < columnCount; col++)
		{
			columnWidths[col] = allRows.Max(r => col < r.Cells.Count ? r.Cells[col].ToPlainText().Length : 0);
			columnWidths[col] = Math.Max(columnWidths[col], 3);
		}

		// When all header cells are empty the table is decorative (e.g. the COMMANDS list).
		// Render it without borders or separator lines: just nicely-spaced columns.
		var headerRows = allRows.Where(r => r.IsHeader).ToList();
		var hasEmptyHeaders = headerRows.Count > 0 &&
			headerRows.All(r => r.Cells.All(c => string.IsNullOrWhiteSpace(c.ToPlainText())));

		if (hasEmptyHeaders)
		{
			// For borderless tables use the full available width split evenly across columns.
			// Column separator is 2 spaces; no pipe characters.
			const int BORDERLESS_SEP_WIDTH = 2;
			var borderlessAvailable = _maxWidth - (columnCount - 1) * BORDERLESS_SEP_WIDTH;
			var totalBorderlessWidth = columnWidths.Sum();

			if (totalBorderlessWidth > borderlessAvailable && borderlessAvailable > columnCount * 3)
			{
				for (var col = 0; col < columnCount; col++)
				{
					var proportion = (double)columnWidths[col] / totalBorderlessWidth;
					columnWidths[col] = Math.Max(3, (int)(borderlessAvailable * proportion));
				}
			}
			else if (totalBorderlessWidth < borderlessAvailable)
			{
				var extraSpace = borderlessAvailable - totalBorderlessWidth;
				for (var col = 0; col < columnCount; col++)
				{
					var proportion = (double)columnWidths[col] / totalBorderlessWidth;
					columnWidths[col] += (int)(extraSpace * proportion);
				}
			}

			var borderlessSpecs = new StringBuilder();
			for (var col = 0; col < columnCount; col++)
			{
				if (col > 0) borderlessSpecs.Append(' ');
				borderlessSpecs.Append('<');
				borderlessSpecs.Append(columnWidths[col]);
			}

			var borderlessRows = allRows
				.Where(r => !r.IsHeader)
				.Select(r => TextAlignerModule.align(
					borderlessSpecs.ToString(),
					r.Cells,
					MModule.single(" "),
					MModule.single("  "),
					MModule.single("")
				))
				.ToList();

			return MModule.multipleWithDelimiter(MModule.single("\n"), borderlessRows);
		}

		// Fit table to available width by distributing space across columns
		// Format: "| cell1 | cell2 | cell3 |"
		// Total width = START_BORDER + content widths + separators + END_BORDER
		var borderAndSeparatorWidth = START_BORDER_WIDTH + END_BORDER_WIDTH +
																	 (columnCount - 1) * COLUMN_SEPARATOR_WIDTH;
		var availableWidth = _maxWidth - borderAndSeparatorWidth;
		var totalWidth = columnWidths.Sum();

		if (totalWidth > availableWidth && availableWidth > columnCount * 3)
		{
			// Scale down column widths proportionally when table is too wide
			for (var col = 0; col < columnCount; col++)
			{
				var proportion = (double)columnWidths[col] / totalWidth;
				columnWidths[col] = Math.Max(3, (int)(availableWidth * proportion));
			}
		}
		else if (totalWidth < availableWidth)
		{
			// Expand columns proportionally to fit the available width for nice spacing
			var extraSpace = availableWidth - totalWidth;
			for (var col = 0; col < columnCount; col++)
			{
				var proportion = (double)columnWidths[col] / totalWidth;
				columnWidths[col] += (int)(extraSpace * proportion);
			}
		}

		// Build column specifications with alignment
		var columnSpecs = new StringBuilder();
		for (var col = 0; col < columnCount; col++)
		{
			if (col > 0) columnSpecs.Append(' ');

			var alignment = "<"; // Default to left
			if (table.ColumnDefinitions.Count > col && table.ColumnDefinitions[col].Alignment.HasValue)
			{
				alignment = table.ColumnDefinitions[col].Alignment!.Value switch
				{
					TableColumnAlign.Left => "<",
					TableColumnAlign.Center => "-",
					TableColumnAlign.Right => ">",
					_ => "<"
				};
			}

			columnSpecs.Append(alignment);
			columnSpecs.Append(columnWidths[col]);
		}

		// Render each row
		var renderedRows = new List<MString>();
		for (var rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
		{
			var (isHeader, cells) = allRows[rowIndex];

			// Use TextAlignerModule to align the cells
			var alignedRow = TextAlignerModule.align(
				columnSpecs.ToString(),
				cells,
				MModule.single(" "),
				MModule.MarkupSingle(borderStyle, " | "),
				MModule.single("")
			);

			// Wrap in borders
			var rowWithBorders = MModule.multiple([
				MModule.MarkupSingle(borderStyle, "| "),
				alignedRow,
				MModule.MarkupSingle(borderStyle, " |")
			]);

			renderedRows.Add(rowWithBorders);

			// Add separator after header
			if (isHeader)
			{
				var separator = new StringBuilder();
				separator.Append("|");
				for (var col = 0; col < columnCount; col++)
				{
					separator.Append('-', columnWidths[col] + 2);
					separator.Append('|');
				}
				renderedRows.Add(MModule.MarkupSingle(borderStyle, separator.ToString()));
			}
		}

		return MModule.multipleWithDelimiter(MModule.single("\n"), renderedRows);
	}

	// Rows are handled by RenderTable for proper alignment
	private MString RenderTableRow(TableRow _)
		=> MModule.empty();

	private MString RenderTableCell(TableCell cell)
		=> MModule.multiple(cell
			.Select(Render)
			.Where(rendered => rendered.Length > 0));
}
