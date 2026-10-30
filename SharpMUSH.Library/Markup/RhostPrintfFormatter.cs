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
/// after it, <c>@</c> leave out a field and the text after it until some field has had a value, <c>&lt;</c> and <c>&gt;</c> empty
/// the field when the one before or after is empty, <c>'</c> and <c>`</c> shift a neighbour into an
/// empty column, <c>#</c> or <c>#n#</c> tabs as spaces, <c>/n/</c> and <c>/wn/</c> a cell or line
/// limit for wrapping, and <c>;</c>, <c>;n;</c>, <c>;n.l;</c> or <c>;n.l+w;</c> a hanging indent.
/// Continuation rows draw the literal text as spaces and stop after the last field that has more
/// than one row.
/// </remarks>
public static class RhostPrintfFormatter
{
	public const string AlignmentRepeated = "#-1 FIELD SPECIFIER EXPECTED [ALIGNMENT PREVIOUSLY SPECIFIED]";

	private enum Omit { Never, WhenEmpty, UntilAValue }

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
		var multiLineSuppress = new List<bool>();
		var fieldIndex = 0;
		var skipLiteral = false;
		var sawValue = false;
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
			sawValue |= !IsEmpty(value);
			// A field with no argument at all is left out under either code.
			if (field.Omit != Omit.Never && index >= values.Count
					|| field.Omit == Omit.WhenEmpty && IsEmpty(value)
					|| field.Omit == Omit.UntilAValue && !sawValue)
			{
				skipLiteral = true;
				continue;
			}

			var columnFormat = field.Natural ? NaturalWidth(field.Format, value) : field.Format;
			if (columnFormat.Width <= 0) continue;
			if (field.WordWrap)
			{
				// The wrap is done here, so the indent and the cell limit are too.
				value = WordWrap(columnFormat.MaxCells > 0 && value.Length > columnFormat.MaxCells ? value.Substring(0, columnFormat.MaxCells) : value,
					columnFormat.Width, columnFormat.Indent);
				columnFormat = columnFormat with { Indent = default, MaxCells = 0 };
			}

			if (field.MultiLine && IsMultiLine(value, columnFormat)) multiLineSuppress.Add(columnFormat.SuppressBlankLast);

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

		// RhostMUSH drops a blank last row when every field that runs past the first row asks for it; a field
		// with one row has no say. The layout asks it of every column, so all of them get the same answer.
		var suppressBlankLast = multiLineSuppress.Count > 0 && multiLineSuppress.All(asked => asked);
		for (var i = 0; i < cells.Count; i++)
		{
			if (cells[i] is LayoutColumn column && column.Format.SuppressBlankLast != suppressBlankLast)
				cells[i] = column with { Format = column.Format with { SuppressBlankLast = suppressBlankLast } };
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
	/// Whether a field RhostMUSH breaks into rows: one with <c>&amp;</c> whose value holds a newline, or one with
	/// <c>|</c> whose value is wider than the field.
	/// </summary>
	private static bool IsMultiLine(MarkupText value, ColumnFormat format)
		=> value.Text.Contains('\n') || (format.Wrap == WrapMode.Cell && value.DisplayWidth > format.Width);

	/// <summary>
	/// RhostMUSH's <c>|"</c> wrap, as lines joined by newlines. A line that does not reach the end of the
	/// text breaks at the last space among its first <paramref name="width"/> characters, which is
	/// then dropped; so a word plus the space after it has to fit. A line with no space there is cut at
	/// the width. Newlines in the text break as well.
	/// </summary>
	/// <remarks>
	/// With an <paramref name="indent"/>, every line after a break at a space has the indent taken off its
	/// width, whether or not it is indented yet, as RhostMUSH counts it; after a cut, only an indented line
	/// does, and so does an indented line after a newline. The indent's spaces start at <see cref="Indent.FromLine"/>.
	/// </remarks>
	private static MarkupText WordWrap(MarkupText value, int width, Indent indent)
	{
		var amount = Math.Clamp(indent.Amount, 0, Math.Max(0, width - 1));
		var firstIndented = Math.Max(1, indent.FromLine);
		var lines = new List<MarkupText>();
		foreach (var segment in value.Split("\n"))
		{
			var rest = segment;
			// A line after a newline that will be indented has the indent taken off its width.
			var room = lines.Count >= firstIndented ? Math.Max(1, width - amount) : width;
			while (rest.Length > room)
			{
				var space = rest.Text.LastIndexOf(' ', room - 1);
				if (space > 0)
				{
					lines.Add(rest.Substring(0, space));
					rest = rest.Substring(space + 1);
					room = Math.Max(1, width - amount + indent.Widen);
				}
				else
				{
					lines.Add(rest.Substring(0, room));
					rest = rest.Substring(room);
					room = lines.Count >= indent.FromLine ? Math.Max(1, width - amount) : width;
				}
			}

			lines.Add(rest);
		}

		if (amount > 0)
		{
			for (var line = firstIndented; line < lines.Count; line++)
				lines[line] = MarkupText.Concat([MarkupText.Space.Repeat(amount), lines[line]]);
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
				case '@': omit = Omit.UntilAValue; break;
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
						// RhostMUSH's .n counts the lines after the first, so the indent starts on line n + 2.
						if (dot.Length > 1 && dot[1].Length > 0) fromLine = SmallNumber(dot[1]) + 1;
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
