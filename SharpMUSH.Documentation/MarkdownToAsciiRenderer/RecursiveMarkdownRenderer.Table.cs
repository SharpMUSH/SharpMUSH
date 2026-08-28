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

		var columnCount = allRows.Max(r => r.Cells.Count);
		var cellsByRow = allRows.Select(r => (IReadOnlyList<MString>)r.Cells).ToList();

		// When all header cells are empty the table is decorative (e.g. the COMMANDS list).
		// Render it without borders or separator lines: just nicely-spaced columns.
		var headerRows = allRows.Where(r => r.IsHeader).ToList();
		var hasEmptyHeaders = headerRows.Count > 0 &&
			headerRows.All(r => r.Cells.All(c => string.IsNullOrWhiteSpace(c.ToPlainText())));

		if (hasEmptyHeaders)
		{
			const int BORDERLESS_SEP_WIDTH = 2;
			var borderlessWidths = ComputeColumnWidths(
				cellsByRow, columnCount, _maxWidth - (columnCount - 1) * BORDERLESS_SEP_WIDTH);

			var borderlessSpecs = new StringBuilder();
			for (var col = 0; col < columnCount; col++)
			{
				if (col > 0) borderlessSpecs.Append(' ');
				borderlessSpecs.Append('<');
				borderlessSpecs.Append(borderlessWidths[col]);
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

		// Fit the table to the width left once the borders are accounted for.
		// Format: "| cell1 | cell2 | cell3 |"
		var columnWidths = ComputeColumnWidths(cellsByRow, columnCount, TableContentWidth(columnCount));

		var columnSpecs = new StringBuilder();
		for (var col = 0; col < columnCount; col++)
		{
			if (col > 0) columnSpecs.Append(' ');

			var alignment = "<";
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

		var renderedRows = new List<MString>();
		for (var rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
		{
			var (isHeader, cells) = allRows[rowIndex];

			var alignedRow = TextAlignerModule.align(
				columnSpecs.ToString(),
				cells,
				MModule.single(" "),
				MModule.MarkupSingle(borderStyle, " | "),
				MModule.single("")
			);

			var rowWithBorders = MModule.multiple([
				MModule.MarkupSingle(borderStyle, "| "),
				alignedRow,
				MModule.MarkupSingle(borderStyle, " |")
			]);

			renderedRows.Add(rowWithBorders);

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

	/// <summary>
	/// The width available to a bordered table's cells, once the outer borders and the
	/// <c>" | "</c> between each pair of columns are taken out.
	/// </summary>
	protected int TableContentWidth(int columnCount) =>
		_maxWidth - (START_BORDER_WIDTH + END_BORDER_WIDTH + (columnCount - 1) * COLUMN_SEPARATOR_WIDTH);

	/// <summary>
	/// The per-column widths the built-in table layout lays a table out to.
	/// </summary>
	/// <remarks>
	/// Shared with <c>RENDERMARKUP`TABLE</c>'s payload, which is the reason it is not inline in
	/// <see cref="RenderTable"/> any more: a template is handed cells as markdown <em>source</em>, and
	/// source length is not rendered length — <c>**index**</c> is nine characters and renders as five.
	/// A template therefore cannot measure its own columns, and a second implementation here would be a
	/// second set of column widths to disagree with the first.
	/// </remarks>
	/// <param name="rows">Every row's <em>rendered</em> cells; ragged rows are read as empty past their end.</param>
	/// <param name="columnCount">The widest row's cell count.</param>
	/// <param name="availableWidth">The width the columns must add up to, borders already deducted.</param>
	protected static int[] ComputeColumnWidths(
		IReadOnlyList<IReadOnlyList<MString>> rows, int columnCount, int availableWidth) =>
		FitColumnWidths(NaturalColumnWidths(rows, columnCount), availableWidth);

	/// <summary>Each column's widest rendered cell, floored at 3 so a column is never unreadable.</summary>
	private static int[] NaturalColumnWidths(IReadOnlyList<IReadOnlyList<MString>> rows, int columnCount) =>
		Enumerable.Range(0, columnCount)
			.Select(col => Math.Max(3, rows.Max(row => col < row.Count ? row[col].ToPlainText().Length : 0)))
			.ToArray();

	/// <summary>
	/// Scales natural widths to the space actually available, in proportion, in either direction.
	/// </summary>
	/// <remarks>
	/// Shrinking is skipped when the space left would not give every column its 3-character floor —
	/// at that point the table cannot fit whatever is done to it, and overflowing is more readable
	/// than clipping every column to nothing.
	/// </remarks>
	private static int[] FitColumnWidths(int[] widths, int availableWidth)
	{
		var total = widths.Sum();
		if (total == 0) return widths;

		if (total > availableWidth && availableWidth > widths.Length * 3)
		{
			for (var col = 0; col < widths.Length; col++)
				widths[col] = Math.Max(3, (int)(availableWidth * ((double)widths[col] / total)));
		}
		else if (total < availableWidth)
		{
			var extraSpace = availableWidth - total;
			for (var col = 0; col < widths.Length; col++)
				widths[col] += (int)(extraSpace * ((double)widths[col] / total));
		}

		return widths;
	}

	// Rows are handled by RenderTable for proper alignment
	private MString RenderTableRow(TableRow _)
		=> MModule.empty();

	/// <summary>
	/// Renders one table cell's contents, inline markup and all.
	/// </summary>
	/// <remarks>
	/// Overridable so a renderer that lays a table out for itself can reuse the cell rendering rather
	/// than re-implementing the inline walk. Column widths are still computed by
	/// <see cref="RenderTable"/> across every row at once, so this is not a hook for cell layout.
	/// </remarks>
	protected virtual MString RenderTableCell(TableCell cell)
		=> MModule.multiple(cell
			.Select(Render)
			.Where(rendered => rendered.Length > 0));
}
