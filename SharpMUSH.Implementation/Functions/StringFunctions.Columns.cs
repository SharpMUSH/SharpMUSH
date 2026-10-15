using System.Text;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's <c>columns()</c>: the words of a text set out in fixed-width columns, down each column
/// first or across each row first, with optional borders, filler and cropping.
/// </summary>
/// <remarks>
/// The layout follows RhostMUSH exactly, including how it treats empty items between delimiters: an
/// empty item is padded to the column width but not ended by a line of its own, so it runs on into the
/// item after it. That is why <c>columns(a@@b@,3,2,L,,,,|,,0,@)</c> puts a blank column beside <c>a</c>
/// and a double-width cell under it.
/// </remarks>
public partial class Functions
{
	private const string ColumnsWidthTooSmall = "#-1 WIDTH MUST BE >= 1";
	private const string ColumnsWidthOutOfBounds = "#-1 WIDTH ARGUMENT OUT OF BOUNDS";
	private const string ColumnsCountOutOfBounds = "#-1 COLUMNS ARGUMENT OUT OF BOUNDS";
	private const string ColumnsInvalidJustification = "#-1 INVALID JUSTIFICATION SPECIFIED";

	private enum ColumnJustification { Left, Right, Center, Spread }

	[SharpFunction(Name = "columns", MinArgs = 3, MaxArgs = 13, Flags = FunctionFlags.Regular,
		ParameterNames = ["text", "width", "columns", "justification", "crop", "cut", "left", "between", "right", "order", "delimiter", "filler", "fill-row"])]
	public ValueTask<CallState> Columns(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ValueTask.FromResult(LayOutColumns(parser));

	private static CallState LayOutColumns(IMUSHCodeParser parser)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var limit = parser.CurrentState.OutputLimit;
		string Text(int index) => args.Count > index ? args[index.ToString()].Message.ToPlainText() : string.Empty;
		MString Markup(int index) => args.Count > index ? args[index.ToString()].Message : MarkupText.Empty;
		bool IsOne(int index) => RhostNumbers.LeadingInteger(Text(index)) == 1;

		var maxWidth = RhostNumbers.LeadingInteger(Text(1));
		if (maxWidth < 1) return new CallState(ColumnsWidthTooSmall);
		if (maxWidth > limit - 1) return new CallState(ColumnsWidthOutOfBounds);

		var columnCount = RhostNumbers.LeadingInteger(Text(2));
		if (columnCount < 1 || columnCount > limit - 1) return new CallState(ColumnsCountOutOfBounds);

		var justificationText = Text(3);
		ColumnJustification? justification = justificationText.Length == 0
			? ColumnJustification.Left
			: char.ToUpperInvariant(justificationText[0]) switch
			{
				'L' => ColumnJustification.Left,
				'R' => ColumnJustification.Right,
				'C' => ColumnJustification.Center,
				'J' => ColumnJustification.Spread,
				_ => null
			};
		if (justification is not { } justify) return new CallState(ColumnsInvalidJustification);

		var filler = new string(Text(11).Where(c => c is not ('\r' or '\n' or '\t')).ToArray());
		var words = new ColumnWords(Text(0), Text(10) is { Length: > 0 } delimiter ? delimiter[0] : (char?)null,
			maxWidth, Crop: IsOne(4), Cut: IsOne(5));
		var cells = new ColumnCells(justify, words.Width(), filler.Length > 0 ? filler : " ");

		var (lines, count) = words.Lines(cells);
		var mostCells = (limit - 2) / (cells.Width + 1) + ((limit - 2) % (cells.Width + 1) > 0 ? 1 : 0);
		count = Math.Min(count, mostCells);

		var output = new ColumnsOutput(limit);
		var borders = new ColumnBorders(Markup(6), Markup(7), Markup(8),
			FillRow: RhostNumbers.LeadingInteger(Text(12)) > 0);
		if (IsOne(9))
		{
			AcrossThenDown(output, lines, count, columnCount, cells, borders);
		}
		else
		{
			DownThenAcross(output, lines, count, columnCount, cells, borders);
		}

		return new CallState(output.Result());
	}

	private readonly record struct ColumnBorders(MString Left, MString Between, MString Right, bool FillRow);

	/// <summary>
	/// The text split into words, each word cut into pieces no wider than the width. Words are
	/// separated by any white space, or by the one delimiter character when there is one.
	/// </summary>
	private sealed record ColumnWords(string Text, char? Delimiter, int MaxWidth, bool Crop, bool Cut)
	{
		private bool IsSeparator(char c) => Delimiter is { } delimiter ? c == delimiter : c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';

		/// <summary>The column width: the width asked for, or with cropping the widest piece there is.</summary>
		public int Width()
		{
			if (!Crop) return MaxWidth;

			var widest = 0;
			var position = 0;
			while (true)
			{
				while (position < Text.Length && IsSeparator(Text[position])) position++;
				if (position == Text.Length) return widest;

				var start = position++;
				while (position < Text.Length && !IsSeparator(Text[position]) && position - start < MaxWidth) position++;
				widest = Math.Max(widest, position - start);
				if (Cut) SkipRestOfWord(ref position);
			}
		}

		/// <summary>
		/// Every cell laid out, one line each, and how many cells count towards the rows. An empty item
		/// between delimiters is a cell that counts but ends no line, and a delimiter at the very start
		/// adds one more that does not count either.
		/// </summary>
		public (string[] Lines, int Count) Lines(ColumnCells cells)
		{
			var held = new StringBuilder();
			var count = 0;
			var position = 0;
			while (position < Text.Length)
			{
				if (Delimiter is { } delimiter)
				{
					if (position == 0 && Text[0] == delimiter) held.Append(cells.Cell(string.Empty));
					if (Text[position] == delimiter && (position + 1 == Text.Length || Text[position + 1] == delimiter))
					{
						count++;
						held.Append(cells.Cell(string.Empty));
						position++;
						continue;
					}
				}

				while (position < Text.Length && IsSeparator(Text[position])) position++;
				if (position == Text.Length) break;

				var start = position++;
				while (position < Text.Length && !IsSeparator(Text[position]) && position - start < MaxWidth) position++;
				count++;
				held.Append(cells.Cell(Text[start..position]));
				if (Cut) SkipRestOfWord(ref position);
				if (position < Text.Length) held.Append('\n');
			}

			return (held.ToString().Split('\n'), count);
		}

		private void SkipRestOfWord(ref int position)
		{
			while (position < Text.Length && !IsSeparator(Text[position])) position++;
		}
	}

	/// <summary>One cell: a piece of text set within the column width, the rest made up of filler.</summary>
	private sealed record ColumnCells(ColumnJustification Justification, int Width, string Filler)
	{
		/// <summary>
		/// <paramref name="length"/> characters of filler, the pattern starting afresh each time.
		/// </summary>
		public string Pad(int length)
		{
			if (length <= 0) return string.Empty;
			var pad = new StringBuilder(length);
			while (pad.Length < length) pad.Append(Filler, 0, Math.Min(Filler.Length, length - pad.Length));
			return pad.ToString();
		}

		public string Cell(string text)
		{
			var room = Width - text.Length;
			return Justification switch
			{
				ColumnJustification.Right => Pad(room) + text,
				ColumnJustification.Center => Pad(room / 2) + text + Pad(room / 2 + room % 2),
				ColumnJustification.Spread => Spread(text),
				_ => text + Pad(room)
			};
		}

		/// <summary>The spaces inside the text widened evenly, the first gaps taking any to spare.</summary>
		private string Spread(string text)
		{
			var spaces = text.Count(c => c == ' ');
			var gap = 0;
			var spare = 0;
			if (spaces > 0)
			{
				var letters = text.Length - spaces;
				gap = (Width - letters) / spaces;
				spare = (Width - letters) % spaces;
				if (gap < 1) (gap, spare) = (1, 0);
			}

			var cell = new StringBuilder();
			foreach (var c in text)
			{
				if (c != ' ')
				{
					cell.Append(c);
					continue;
				}

				cell.Append(Pad(gap));
				if (spare-- > 0) cell.Append(Pad(1));
			}

			return cell.Append(Pad(Width - cell.Length)).ToString();
		}
	}

	/// <summary>The result, held to the output limit.</summary>
	private sealed class ColumnsOutput(int limit)
	{
		private readonly List<MString> _parts = [];
		private int _length;

		/// <summary>Whether the result has reached the limit, so nothing more needs laying out.</summary>
		public bool Full => _length >= limit;

		public void Add(MString part)
		{
			if (Full || part.Length == 0) return;
			_parts.Add(part);
			_length += part.Length;
		}

		public void Add(string part) => Add(MarkupText.Plain(part));

		public MString Result()
		{
			var result = MarkupText.Concat(_parts);
			return result.Length > limit ? result.Substring(0, limit) : result;
		}
	}

	/// <summary>
	/// The cells in order down the first column, then down the next. The first columns take one more
	/// cell each when the cells do not fill the last row.
	/// </summary>
	private static void DownThenAcross(ColumnsOutput output, string[] lines, int count, int columnCount,
		ColumnCells cells, ColumnBorders borders)
	{
		var fullRows = count / columnCount;
		var remainder = count % columnCount;
		var rows = fullRows + (remainder > 0 ? 1 : 0);

		for (var row = 0; row < rows && !output.Full; row++)
		{
			output.Add(borders.Left);
			var column = 0;
			for (; column < columnCount && !output.Full; column++)
			{
				var position = row + column * fullRows + Math.Min(column, remainder);
				var line = position < lines.Length ? lines[position] : string.Empty;
				output.Add(line.Length > 0 && line.Length >= cells.Width - 1 ? line : cells.Pad(cells.Width));

				if (row == rows - 1 && remainder > 0)
				{
					if (remainder - column == 1) break;
					output.Add(borders.Between);
				}
				else if (column < columnCount - 1)
				{
					output.Add(borders.Between);
				}
			}

			FillRow(output, column, columnCount, cells, borders);
			output.Add(borders.Right);
			if (row < rows - 1) output.Add(MarkupText.NewLine);
		}
	}

	/// <summary>The cells in order along the first row, then along the next.</summary>
	private static void AcrossThenDown(ColumnsOutput output, string[] lines, int count, int columnCount,
		ColumnCells cells, ColumnBorders borders)
	{
		var next = 0;
		while (count > 0 && !output.Full)
		{
			output.Add(borders.Left);
			var column = 0;
			for (; column < columnCount; column++)
			{
				count--;
				output.Add(lines[next]);
				// The last line is used again when the cells outnumber the lines.
				if (next < lines.Length - 1) next++;
				if (count == 0) break;
				if (column < columnCount - 1) output.Add(borders.Between);
			}

			FillRow(output, column, columnCount, cells, borders);
			output.Add(borders.Right);
			if (count > 0) output.Add(MarkupText.NewLine);
		}
	}

	/// <summary>
	/// With the fill-row argument, the columns a short row leaves out are made up with blank cells.
	/// <paramref name="lastColumn"/> is the column the row stopped at.
	/// </summary>
	private static void FillRow(ColumnsOutput output, int lastColumn, int columnCount, ColumnCells cells, ColumnBorders borders)
	{
		if (!borders.FillRow) return;
		for (var filled = Math.Min(lastColumn + 1, columnCount); filled < columnCount && !output.Full; filled++)
		{
			output.Add(borders.Between);
			output.Add(cells.Pad(cells.Width));
		}
	}
}
