using Markdig.Extensions.Tables;
using MarkupString;
using SharpMUSH.Library.Markup;
using System.Collections.Immutable;
using System.Text;
using MarkupString.Layout;
using Block = MarkupString.Layout.Block;
using Table = Markdig.Extensions.Tables.Table;
using LayoutTable = MarkupString.Layout.Table;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	/// <summary>
	/// A table with headings as a layout <see cref="LayoutTable"/>; one whose headings are all empty
	/// as plain columns, the way a topic list is written.
	/// </summary>
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

		if (allRows.Count == 0) return MarkupText.Empty;

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
			// A list keeps its columns as narrow as their names, like PennMUSH's topic lists; only one
			// too wide for the line is squeezed to fit.
			var naturalWidths = NaturalColumnWidths(cellsByRow, columnCount);
			var borderlessWidth = _maxWidth - (columnCount - 1) * BORDERLESS_SEP_WIDTH;
			var borderlessWidths = naturalWidths.Sum() <= borderlessWidth
				? naturalWidths
				: FitColumnWidths(naturalWidths, borderlessWidth);

			var borderlessSpecs = new StringBuilder();
			for (var col = 0; col < columnCount; col++)
			{
				if (col > 0) borderlessSpecs.Append(' ');
				borderlessSpecs.Append('<');
				borderlessSpecs.Append(borderlessWidths[col]);
			}

			var borderlessRows = allRows
				.Where(r => !r.IsHeader)
				.Select(r => TextAligner.Align(
					borderlessSpecs.ToString(),
					r.Cells,
					MarkupText.Plain(" "),
					MarkupText.Plain("  "),
					MarkupText.Plain("\n")
				))
				.ToList();

			// Laid out by its own spacing, which is what Preformatted says: a Pueblo client reads the
			// stream as HTML, where runs of spaces collapse, and the portal gets a <pre>.
			return MarkupText.Preformatted(MarkupText.Join(MarkupText.Plain("\n"), borderlessRows));
		}

		// A table with headings is a layout table: columns sized to their widest cell, wrapping and then
		// leaving out columns on a narrow client, a card per row when not even one fits, and a real
		// <table> in the portal.
		// A heading is drawn on one line, so a column narrows no further than its heading's longest word.
		var columns = Enumerable.Range(0, columnCount)
			.Select(col => headerRows.Count > 0 && col < headerRows[0].Cells.Count ? headerRows[0].Cells[col] : MarkupText.Empty)
			.Select((heading, col) => new TableColumn(heading)
			{
				Alignment = table.ColumnDefinitions.Count > col
					? table.ColumnDefinitions[col].Alignment switch
					{
						TableColumnAlign.Center => Alignment.Center,
						TableColumnAlign.Right => Alignment.Right,
						_ => Alignment.Left
					}
					: Alignment.Left,
				Min = Math.Max(MinimumColumnWidth, heading.ToPlainText().Split(' ').Max(word => word.Length))
			})
			.ToImmutableArray();
		var body = allRows
			.Where(r => !r.IsHeader)
			.Select(r => r.Cells.Select(cell => (Block)cell).ToImmutableArray())
			.ToImmutableArray();

		return Laid(new LayoutTable(columns, body)
		{
			Separator = MarkupText.Wrap(borderStyle, " | "),
			HeaderRule = MarkupText.Wrap(borderStyle, "-")
		});
	}

	/// <summary>
	/// The width available to a table's cells once the <c>" | "</c> between each pair of columns and
	/// the borders a <c>RENDERMARKUP`TABLE</c> template draws on either side are taken out.
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

	/// <summary>Narrowest a column may be squeezed to before the table is left to overflow.</summary>
	private const int MinimumColumnWidth = 3;

	/// <summary>Each column's widest rendered cell, floored at 3 so a column is never unreadable.</summary>
	private static int[] NaturalColumnWidths(IReadOnlyList<IReadOnlyList<MString>> rows, int columnCount) =>
		Enumerable.Range(0, columnCount)
			.Select(col => Math.Max(MinimumColumnWidth, rows.Max(row => col < row.Count ? row[col].ToPlainText().Length : 0)))
			.ToArray();

	/// <summary>
	/// Scales natural widths to fit <paramref name="availableWidth"/> exactly, in either direction.
	/// </summary>
	/// <remarks>
	/// Shrinking is skipped when the space left would not give every column its floor — at that point
	/// the table cannot fit whatever is done to it, and overflowing is more readable than clipping
	/// every column to nothing.
	/// </remarks>
	private static int[] FitColumnWidths(int[] widths, int availableWidth)
	{
		var total = widths.Sum();
		if (total == 0) return widths;
		if (total > availableWidth && availableWidth <= widths.Length * MinimumColumnWidth) return widths;

		return Apportion(widths, availableWidth);
	}

	/// <summary>
	/// Largest-remainder (Hamilton) apportionment of <paramref name="availableWidth"/> characters
	/// across the columns, in proportion to their natural widths.
	/// </summary>
	/// <remarks>
	/// Scaling each column independently and truncating leaves the total off the budget, so the
	/// leftover characters have to go somewhere. Handing them to the largest fractional remainders is
	/// the standard answer to this — the same apportionment problem as allocating seats to
	/// populations — and is the only rule that keeps every column within one character of its exact
	/// share. The floor can push the total back over the budget, in which case the excess comes off
	/// the smallest remainders first, never below the floor.
	/// </remarks>
	private static int[] Apportion(int[] widths, int availableWidth)
	{
		var total = widths.Sum();
		var byRemainder = new (int Column, double Fraction)[widths.Length];
		var assigned = 0;

		for (var col = 0; col < widths.Length; col++)
		{
			var exact = availableWidth * ((double)widths[col] / total);
			widths[col] = Math.Max(MinimumColumnWidth, (int)exact);
			assigned += widths[col];
			byRemainder[col] = (col, exact - Math.Truncate(exact));
		}

		var order = byRemainder
			.OrderByDescending(entry => entry.Fraction)
			.ThenBy(entry => entry.Column)
			.Select(entry => entry.Column)
			.ToArray();

		for (var i = 0; assigned < availableWidth; i++, assigned++) widths[order[i % order.Length]]++;

		while (assigned > availableWidth)
		{
			var reduced = false;
			for (var i = order.Length - 1; i >= 0 && assigned > availableWidth; i--)
			{
				if (widths[order[i]] <= MinimumColumnWidth) continue;

				widths[order[i]]--;
				assigned--;
				reduced = true;
			}

			if (!reduced) return widths;
		}

		return widths;
	}


	// Rows are handled by RenderTable for proper alignment
	private MString RenderTableRow(TableRow _)
		=> MarkupText.Empty;

	/// <summary>
	/// Renders one table cell's contents, inline markup and all.
	/// </summary>
	/// <remarks>
	/// Overridable so a renderer that lays a table out for itself can reuse the cell rendering rather
	/// than re-implementing the inline walk. Column widths are still computed by
	/// <see cref="RenderTable"/> across every row at once, so this is not a hook for cell layout.
	/// </remarks>
	protected virtual MString RenderTableCell(TableCell cell)
		=> MarkupText.Concat(cell
			.Select(Render)
			.Where(rendered => rendered.Length > 0));
}
