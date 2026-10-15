using System.Runtime.InteropServices;
using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// printf() with <c>rhost_printf</c> on: RhostMUSH's <c>$&lt;codes&gt;s</c> field grammar, mapped onto the
/// MarkupString column layout. The layout does the drawing; this reads the format and says what each
/// field asks for.
/// </summary>
/// <remarks>
/// A format is literal text with fields in it. A field starts at <c>$</c> and ends at the next <c>s</c>;
/// <c>$$</c> is a literal dollar sign. The codes between, in any order: a width (a leading 0 pads with
/// zeros), <c>-</c> left, <c>^</c> centre and <c>_</c> full justification (right by default), <c>+</c>
/// no cut, <c>*</c> cut the start rather than the end, <c>&amp;</c> one row per line of the value,
/// <c>|</c> wrap at the width, <c>"</c> wrap at a space, <c>:fill:</c> (<c>:!fill:</c> leaves blank
/// lines unfilled), <c>.</c> drop a blank last row, <c>!</c> leave out an empty field and the text
/// after it, <c>@</c> the same when the field before was empty too, <c>&lt;</c> and <c>&gt;</c> empty
/// the field when the one before or after is empty, <c>'</c> and <c>`</c> shift a neighbour into an
/// empty column, <c>#</c> or <c>#n#</c> tabs as spaces, <c>/n/</c> and <c>/wn/</c> a cell or line
/// limit for wrapping, and <c>;</c>, <c>;n;</c>, <c>;n.l;</c> or <c>;n.l+w;</c> a hanging indent.
/// Continuation rows draw the literal text as spaces and stop after the last field that has more
/// than one row.
/// </remarks>
public static class RhostPrintfFormatter
{
	public const string AlignmentRepeated = "#-1 FIELD SPECIFIER EXPECTED [ALIGNMENT PREVIOUSLY SPECIFIED]";

	private enum Omit { Never, WhenEmpty, WhenThisOrPreviousEmpty }

	private sealed record Field(ColumnFormat Format, bool Natural, bool MultiLine, Omit Omit,
		bool EmptyIfPreviousEmpty, bool EmptyIfNextEmpty, bool WordWrap);

	private sealed record Piece(MarkupText? Literal, Field? Field);

	public static bool TryFormat(MarkupText format, IReadOnlyList<MarkupText> values, out MarkupText result,
		out string? error, int maxOutput = FunctionLimits.MaxOutputCodeUnits)
	{
		result = MarkupText.Empty;
		if (!TryParse(format, out var pieces, out error)) return false;

		var fields = pieces.Where(piece => piece.Field is not null).Select(piece => piece.Field!).ToList();
		var given = fields.Select((_, index) => index < values.Count ? values[index] : MarkupText.Empty).ToArray();
		var effective = given.Select((value, index) =>
			(fields[index].EmptyIfPreviousEmpty && index > 0 && IsEmpty(given[index - 1]))
			|| (fields[index].EmptyIfNextEmpty && index + 1 < given.Length && IsEmpty(given[index + 1]))
				? MarkupText.Empty
				: value).ToArray();

		var cells = new List<LayoutCell>();
		var lastMultiLine = -1;
		var fieldIndex = 0;
		var skipLiteral = false;
		foreach (var piece in pieces)
		{
			if (piece.Literal is { } literal)
			{
				if (!skipLiteral && literal.Length > 0) cells.Add(new LayoutSeparator(literal, SeparatorRows.FirstRowOnly));
				skipLiteral = false;
				continue;
			}

			var field = piece.Field!;
			var index = fieldIndex++;
			var value = effective[index];
			if ((field.Omit == Omit.WhenEmpty && IsEmpty(value))
					|| (field.Omit == Omit.WhenThisOrPreviousEmpty && (IsEmpty(value) || (index > 0 && IsEmpty(effective[index - 1])))))
			{
				skipLiteral = true;
				continue;
			}

			var columnFormat = field.Natural ? NaturalWidth(field.Format, value) : field.Format;
			if (columnFormat.Width <= 0) continue;
			if (field.WordWrap)
			{
				value = WordWrap(value, columnFormat.Width);
			}

			// Full justification has no gap to widen in a single word; RhostMUSH centres that word.
			if (columnFormat.Alignment == Alignment.Full && value.ToPlainText().Split(" ", StringSplitOptions.RemoveEmptyEntries).Length < 2)
			{
				columnFormat = columnFormat with { Alignment = Alignment.Center };
			}

			cells.Add(new LayoutColumn(value, columnFormat));
			if (field.MultiLine) lastMultiLine = cells.Count - 1;
		}

		if (!cells.Any(cell => cell is LayoutColumn))
		{
			result = MarkupText.Concat(cells.OfType<LayoutSeparator>().Select(separator => separator.Text).ToArray());
			error = null;
			return true;
		}

		var span = CollectionsMarshal.AsSpan(cells);
		var rows = TextLayout.Rows(span, new LayoutOptions()).ToArray();

		// RhostMUSH ends a continuation row at its last field with more than one row: the single-row
		// fields and the text after that field belong to the first row only.
		var continuationWidth = lastMultiLine < 0 ? 0 : span[..(lastMultiLine + 1)].ToArray().Sum(CellWidth);
		var lines = new List<MarkupText>(rows.Length);
		var length = 0L;
		for (var row = 0; row < rows.Length; row++)
		{
			var line = row == 0 || continuationWidth == 0 ? rows[row] : rows[row].TruncateToWidth(continuationWidth, CutFrom.End);
			length += line.Length + (row > 0 ? 1 : 0);
			if (length > maxOutput)
			{
				error = ErrorMessages.Returns.OutputTooLarge;
				return false;
			}

			lines.Add(line);
		}

		result = MarkupText.Join(MarkupText.Plain("\n"), lines);
		error = null;
		return true;

		int CellWidth(LayoutCell cell) => cell switch
		{
			LayoutColumn column => column.Format.Width,
			LayoutSeparator separator => separator.Text.DisplayWidth,
			_ => 0
		};
	}

	private static bool IsEmpty(MarkupText value) => value.Length == 0;

	/// <summary>
	/// RhostMUSH's <c>|"</c> wrap, as lines joined by newlines. A line that does not reach the end of the
	/// text breaks at the last space among its first <paramref name="width"/> characters, which is
	/// then dropped; so a word plus the space after it has to fit. A line with no space there is cut at
	/// the width. Newlines in the text break as well.
	/// </summary>
	private static MarkupText WordWrap(MarkupText value, int width)
	{
		var lines = new List<MarkupText>();
		foreach (var segment in value.Split("\n"))
		{
			var rest = segment;
			while (rest.Length > width)
			{
				var space = rest.Text.LastIndexOf(' ', width - 1);
				if (space > 0)
				{
					lines.Add(rest.Substring(0, space));
					rest = rest.Substring(space + 1);
				}
				else
				{
					lines.Add(rest.Substring(0, width));
					rest = rest.Substring(width);
				}
			}

			lines.Add(rest);
		}

		return MarkupText.Join(MarkupText.Plain("\n"), lines);
	}

	/// <summary>A field with no width: as wide as its widest line, so nothing is cut or padded.</summary>
	private static ColumnFormat NaturalWidth(ColumnFormat format, MarkupText value)
	{
		var widest = value.Text.Split('\n').Select(line => MarkupText.Plain(line.TrimEnd('\r')).DisplayWidth).DefaultIfEmpty(0).Max();
		return format with { Width = Math.Min(widest, PrintfFormatter.MaxFieldMeasure) };
	}

	private static bool TryParse(MarkupText format, out List<Piece> pieces, out string? error)
	{
		pieces = [];
		error = null;
		var text = format.Text;
		if (text.Length > PrintfFormatter.MaxFormatCodeUnits)
		{
			error = PrintfFormatter.FieldLimitExceeded;
			return false;
		}

		var literalStart = 0;
		var literal = new List<MarkupText>();
		var position = 0;
		while (position < text.Length)
		{
			var dollar = text.IndexOf('$', position);
			if (dollar < 0) break;

			literal.Add(format.Substring(literalStart, dollar - literalStart));
			if (dollar + 1 < text.Length && text[dollar + 1] == '$')
			{
				literal.Add(format.Substring(dollar, 1));
				position = literalStart = dollar + 2;
				continue;
			}

			var parsed = TryParseField(format, dollar + 1, out var field, out var end, out error);
			if (error is not null) return false;
			if (!parsed)
			{
				// A field never closed by its 's' ends the format: RhostMUSH writes nothing from it on.
				literalStart = position = text.Length;
				break;
			}

			pieces.Add(new Piece(MarkupText.Concat(CollectionsMarshal.AsSpan(literal)), null));
			literal.Clear();
			pieces.Add(new Piece(null, field));
			if (pieces.Count(piece => piece.Field is not null) > PrintfFormatter.MaxFields)
			{
				error = PrintfFormatter.FieldLimitExceeded;
				return false;
			}

			position = literalStart = end;
		}

		if (literalStart < text.Length) literal.Add(format.Substring(literalStart));
		pieces.Add(new Piece(MarkupText.Concat(CollectionsMarshal.AsSpan(literal)), null));
		return true;
	}

	/// <summary>
	/// The codes from <paramref name="start"/> to the closing <c>s</c>. False, with no error, when the
	/// format ends first.
	/// </summary>
	private static bool TryParseField(MarkupText format, int start, out Field field, out int end, out string? error)
	{
		var text = format.Text;
		field = null!;
		end = start;
		error = null;

		Alignment? alignment = null;
		int? width = null;
		var zero = false;
		var overflow = false;
		var cutStart = false;
		var lines = false;
		var wrap = false;
		var words = false;
		var suppressBlankLast = false;
		var omit = Omit.Never;
		var emptyIfPrevious = false;
		var emptyIfNext = false;
		var whenEmpty = WhenEmpty.None;
		var tabWidth = 0;
		var maxCells = 0;
		var maxLines = 0;
		Indent? indent = null;
		MarkupText? fill = null;
		var blankSpaces = false;

		var position = start;
		while (position < text.Length)
		{
			var code = text[position++];
			switch (code)
			{
				case 's':
					var multiLine = lines || wrap;
					var format1 = new ColumnFormat
					{
						Width = width ?? 0,
						Alignment = alignment ?? Alignment.Right,
						Wrap = wrap ? (words ? WrapMode.HardBreaks : WrapMode.Cell) : lines ? WrapMode.HardBreaks : WrapMode.None,
						BreakSpace = BreakSpace.Keep,
						Truncation = overflow ? TruncationType.Overflow : TruncationType.Truncate,
						CutFrom = cutStart ? CutFrom.Start : CutFrom.End,
						Fill = fill ?? (zero && (alignment ?? Alignment.Right) == Alignment.Right ? MarkupText.Plain("0") : MarkupText.Space),
						BlankLineFill = blankSpaces ? BlankLineFill.Spaces : BlankLineFill.Pattern,
						SuppressBlankLast = suppressBlankLast,
						WhenEmpty = whenEmpty,
						TabWidth = tabWidth,
						MaxCells = maxCells,
						MaxLines = maxLines,
					};
					if (indent is { } hanging) format1 = format1 with { Indent = hanging };
					field = new Field(format1, width is null, multiLine, omit, emptyIfPrevious, emptyIfNext, wrap && words);
					end = position;
					return true;
				case >= '0' and <= '9':
					if (code == '0' && width is null && !zero)
					{
						zero = true;
						break;
					}

					var digits = (width ?? 0) * 10 + (code - '0');
					if (digits > PrintfFormatter.MaxFieldMeasure)
					{
						error = PrintfFormatter.FieldLimitExceeded;
						return false;
					}

					width = digits;
					break;
				case '-' or '^' or '_':
					if (alignment is not null)
					{
						error = AlignmentRepeated;
						return false;
					}

					alignment = code switch { '-' => Alignment.Left, '^' => Alignment.Center, _ => Alignment.Full };
					break;
				case '+': overflow = true; break;
				case '*': cutStart = true; break;
				case '&': lines = true; break;
				case '|': wrap = true; break;
				case '"': words = true; break;
				case '.': suppressBlankLast = true; break;
				case '!': omit = Omit.WhenEmpty; break;
				case '@': omit = Omit.WhenThisOrPreviousEmpty; break;
				case '<': emptyIfPrevious = true; break;
				case '>': emptyIfNext = true; break;
				case '\'': whenEmpty = WhenEmpty.PullRightColumnLeft; break;
				case '`': whenEmpty = WhenEmpty.PushLeftColumnRight; break;
				case ':':
					var close = text.IndexOf(':', position);
					if (close < 0) return false;
					var fillText = format.Substring(position, close - position);
					if (fillText.Text.StartsWith('!'))
					{
						blankSpaces = true;
						fillText = fillText.Substring(1);
					}

					fill = fillText.Length == 0 ? MarkupText.Space : fillText;
					position = close + 1;
					break;
				case '/':
					var slash = text.IndexOf('/', position);
					if (slash < 0) return false;
					var limit = text[position..slash];
					if (limit.StartsWith('w') || limit.StartsWith('W'))
						maxLines = Math.Max(0, SmallNumber(limit[1..]));
					else
						maxCells = Math.Max(0, SmallNumber(limit));
					position = slash + 1;
					break;
				case '#':
					tabWidth = 4;
					var hash = text.IndexOf('#', position);
					if (hash > position && text[position..hash].All(char.IsAsciiDigit))
					{
						tabWidth = Math.Clamp(SmallNumber(text[position..hash]), 1, 10);
						position = hash + 1;
					}

					break;
				case ';':
					var semicolon = text.IndexOf(';', position);
					var amount = 4;
					var fromLine = 1;
					var widen = 0;
					if (semicolon >= position && text[position..semicolon].All(c => char.IsAsciiDigit(c) || c is '.' or '+'))
					{
						var spec = text[position..semicolon];
						var plus = spec.Split('+', 2);
						var dot = plus[0].Split('.', 2);
						if (dot[0].Length > 0) amount = SmallNumber(dot[0]);
						if (dot.Length > 1 && dot[1].Length > 0) fromLine = SmallNumber(dot[1]);
						if (plus.Length > 1 && plus[1].Length > 0) widen = SmallNumber(plus[1]);
						position = semicolon + 1;
					}

					indent = new Indent(Math.Clamp(amount, 0, 70), Math.Max(0, fromLine), Math.Max(0, widen));
					break;
			}
		}

		return false;
	}

	private static int SmallNumber(string digits)
		=> int.TryParse(digits, out var number) ? Math.Min(number, PrintfFormatter.MaxFieldMeasure) : 0;
}
