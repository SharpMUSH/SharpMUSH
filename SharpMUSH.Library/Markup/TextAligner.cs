using MarkupString;

// The MarkupText.DisplayWidth instance property shadows the type of the same name, so the
// measuring helpers are reached through this alias.
using Cells = MarkupString.DisplayWidth;

namespace SharpMUSH.Library.Markup;

using ColumnState = (ColumnSpec Spec, MarkupText Text);
using LineResult = (ColumnSpec Spec, MarkupText Remainder, MarkupText Line);

/// <summary>
/// The column layout engine behind the <c>align()</c> function: wraps, truncates, merges and
/// justifies a set of columns into rows. Widths are display cells, not code units, so wide
/// characters take the two columns they occupy on a terminal.
/// </summary>
public static class TextAligner
{
	/// <summary>
	/// Where to break a line that does not fit: the last space at or before the width, else the
	/// widest prefix that fits.
	/// </summary>
	private static (int SplitPoint, bool FoundSpace) FindWrapPoint(MarkupText text, int width)
	{
		var plain = text.Text.AsSpan();
		if (plain.Length == 0) return (0, false);

		var limit = Cells.IndexAtWidth(plain, width);
		var searchStart = Math.Min(limit, plain.Length - 1);

		for (var i = searchStart; i >= 0; i--)
		{
			if (plain[i] == ' ') return (i, true);
		}

		return (limit, false);
	}

	private static MarkupText ApplyRepeatOption(ColumnSpec spec, MarkupText text, MarkupText remainder) =>
		spec.Options.HasFlag(ColumnOptions.Repeat) && remainder.Length == 0 && text.Length > 0
			? text
			: remainder;

	private static (MarkupText Line, MarkupText Remainder) ExtractLineWithNewline(
		ColumnSpec spec, MarkupText text, int rowSepIndex)
	{
		var lineText = text.Substring(0, rowSepIndex);
		var remainder = rowSepIndex + 1 < text.Length
			? text.Substring(rowSepIndex + 1)
			: MarkupText.Empty;
		return (lineText, ApplyRepeatOption(spec, text, remainder));
	}

	private static (MarkupText Line, MarkupText Remainder) ExtractLineFitting(ColumnSpec spec, MarkupText text)
	{
		var remainder = spec.Options.HasFlag(ColumnOptions.Repeat) ? text : MarkupText.Empty;
		return (text, remainder);
	}

	private static (MarkupText Line, MarkupText Remainder) ExtractLineWithWrap(ColumnSpec spec, MarkupText text)
	{
		var (splitPoint, foundSpace) = FindWrapPoint(text, spec.Width);

		var lineText = text.Substring(0, splitPoint);
		var remainderStart = foundSpace && splitPoint < text.Length ? splitPoint + 1 : splitPoint;
		var remainder = remainderStart < text.Length
			? text.Substring(remainderStart)
			: MarkupText.Empty;
		return (lineText, ApplyRepeatOption(spec, text, remainder));
	}

	private static (MarkupText Line, MarkupText Remainder) ExtractLineTruncated(
		ColumnSpec spec, MarkupText text, int rowSepIndex)
	{
		var splitPoint = rowSepIndex >= 0 && rowSepIndex < spec.Width
			? rowSepIndex
			: text.DisplayWidth > spec.Width ? Cells.IndexAtWidth(text.Text, spec.Width) : text.Length;
		return (text.Substring(0, splitPoint), MarkupText.Empty);
	}

	/// <summary>The next line of a column and what is left of it afterwards.</summary>
	public static (MarkupText Line, MarkupText Remainder) ExtractLine(ColumnSpec spec, MarkupText text)
	{
		if (text.Length == 0) return (MarkupText.Empty, MarkupText.Empty);
		if (spec.Options.HasFlag(ColumnOptions.TruncateV2)) return (text, MarkupText.Empty);

		var rowSepIndex = text.IndexOf("\n");
		if (rowSepIndex >= 0 && rowSepIndex < spec.Width)
			return ExtractLineWithNewline(spec, text, rowSepIndex);
		if (spec.Options.HasFlag(ColumnOptions.Truncate))
			return ExtractLineTruncated(spec, text, rowSepIndex);
		if (text.DisplayWidth <= spec.Width)
			return ExtractLineFitting(spec, text);
		return ExtractLineWithWrap(spec, text);
	}

	/// <summary>Pads <paramref name="text"/> out to <paramref name="width"/> cells of fill.</summary>
	public static MarkupText Justify(Justification justification, MarkupText text, int width, MarkupText fill)
	{
		var padType = justification switch
		{
			Justification.Left => PadType.Right,
			Justification.Center => PadType.Center,
			Justification.Full => PadType.Full,
			Justification.Right or Justification.Paragraph => PadType.Left,
			_ => throw new NotSupportedException(),
		};
		return text.Pad(fill, width, padType, TruncationType.Truncate);
	}

	private static List<ColumnState> MergeColumnLeft(List<ColumnState> columns, int index, ColumnSpec spec)
	{
		var result = new List<ColumnState>(columns);
		var leftState = result[index - 1];
		var leftSpec = leftState.Spec;

		var newOptions = leftSpec.Options;
		if (spec.Options.HasFlag(ColumnOptions.NoFill)) newOptions |= ColumnOptions.NoFill;
		if (spec.Options.HasFlag(ColumnOptions.NoColSep)) newOptions |= ColumnOptions.NoColSep;

		var newWidth = leftSpec.Width + spec.Width - 2;
		result[index - 1] = (leftSpec with { Width = newWidth, Options = newOptions }, leftState.Text);
		result[index] = (spec, MarkupText.Empty);
		return result;
	}

	private static List<ColumnState> MergeColumnRight(List<ColumnState> columns, int index, ColumnSpec spec)
	{
		var result = new List<ColumnState>(columns);
		var rightState = result[index + 1];
		var rightSpec = rightState.Spec;
		result[index + 1] = (rightSpec with { Width = rightSpec.Width + spec.Width + 1 }, rightState.Text);
		result[index] = (spec, MarkupText.Empty);
		return result;
	}

	private static List<ColumnState> HandleMerging(List<ColumnState> columns, int index)
	{
		var (spec, text) = columns[index];
		if (text.Length == 0 && spec.Options.HasFlag(ColumnOptions.MergeToLeft) && index > 0)
			return MergeColumnLeft(columns, index, spec);
		if (text.Length == 0 && spec.Options.HasFlag(ColumnOptions.MergeToRight) && index < columns.Count - 1)
			return MergeColumnRight(columns, index, spec);
		return columns;
	}

	private static bool MoreToDo(List<ColumnState> columns) =>
		columns.Any(cs => cs.Text.Length > 0 && !cs.Spec.Options.HasFlag(ColumnOptions.Repeat));

	private static MarkupText JustifyColumnLine(ColumnSpec spec, MarkupText line, MarkupText filler) =>
		spec.Options.HasFlag(ColumnOptions.NoFill)
			? line
			: Justify(spec.Justification, line, spec.Width, filler);

	private static IEnumerable<MarkupText> BuildOutputParts(
		List<LineResult> lineResults, MarkupText columnSeparator, MarkupText filler)
	{
		for (var i = 0; i < lineResults.Count; i++)
		{
			var (spec, _, line) = lineResults[i];
			yield return JustifyColumnLine(spec, line, filler);

			var needsSeparator = i < lineResults.Count - 1 && !spec.Options.HasFlag(ColumnOptions.NoColSep);
			if (!needsSeparator) continue;

			if (i > 0)
			{
				var prevSpec = lineResults[i - 1].Spec;
				if (prevSpec.Options.HasFlag(ColumnOptions.MergeToRight))
				{
					var extraPadding = MarkupText.Plain(new string(' ', prevSpec.Width));
					yield return MarkupText.Concat(extraPadding, columnSeparator);
					continue;
				}
			}
			yield return columnSeparator;
		}
	}

	private static (List<ColumnState> Remainders, MarkupText OutputLine) DoLine(
		List<ColumnState> columns, MarkupText filler, MarkupText columnSeparator)
	{
		var mergedColumns = columns;
		for (var i = 0; i < mergedColumns.Count; i++)
			mergedColumns = HandleMerging(mergedColumns, i);

		var lineResults = new List<LineResult>(mergedColumns.Count);
		foreach (var (spec, text) in mergedColumns)
		{
			var (line, remainder) = ExtractLine(spec, text);
			lineResults.Add((spec, remainder, line));
		}

		var filteredLineResults = lineResults
			.Where(lr => !(lr.Line.Length == 0 &&
				(lr.Spec.Options.HasFlag(ColumnOptions.MergeToLeft) ||
				 lr.Spec.Options.HasFlag(ColumnOptions.MergeToRight))))
			.ToList();

		var outputParts = BuildOutputParts(filteredLineResults, columnSeparator, filler);
		var outputLine = MarkupText.Concat(outputParts);
		var remainders = filteredLineResults.Select(lr => (lr.Spec, lr.Remainder)).ToList();

		return (remainders, outputLine);
	}

	private static MarkupText AlignLoop(
		List<ColumnState> columns, MarkupText filler, MarkupText columnSeparator, MarkupText rowSeparator)
	{
		var accumulator = new List<MarkupText>();
		while (MoreToDo(columns))
		{
			var (remainder, newLine) = DoLine(columns, filler, columnSeparator);
			accumulator.Add(newLine);
			columns = remainder;
		}
		return MarkupText.Join(rowSeparator, accumulator);
	}

	private static MarkupText? ValidateParameters(
		List<ColumnSpec> columnSpecs, List<MarkupText> columns, MarkupText filler)
	{
		if (columnSpecs.Count != columns.Count)
			return MarkupText.Plain("#-1 COLUMN COUNT MISMATCH");
		if (filler.Length > 1)
			return MarkupText.Plain("#-1 FILLER MUST BE ONE CHARACTER");
		if (columnSpecs.Any(s => s.Width <= 0))
			return MarkupText.Plain("#-1 CANNOT HAVE COLUMNS OF NEGATIVE SIZE");
		if (columnSpecs.Any(s => s.Width > 5_000_000))
			return MarkupText.Plain("#-1 CANNOT HAVE COLUMNS THAT LARGE");
		if (columnSpecs.Any(s =>
				s.Options.HasFlag(ColumnOptions.Repeat) &&
				(s.Options.HasFlag(ColumnOptions.Truncate) || s.Options.HasFlag(ColumnOptions.TruncateV2))))
			return MarkupText.Plain("#-1 CANNOT REPEAT AND TRUNCATE");
		return null;
	}

	/// <summary>Lays <paramref name="columns"/> out against the <paramref name="widths"/> spec.</summary>
	public static MarkupText Align(
		string widths,
		IEnumerable<MarkupText> columns,
		MarkupText filler,
		MarkupText columnSeparator,
		MarkupText rowSeparator)
	{
		var columnSpecs = ColumnSpecParser.ParseList(widths);
		var colList = columns.ToList();

		var error = ValidateParameters(columnSpecs, colList, filler);
		if (error is not null) return error;

		var cols = columnSpecs.Zip(colList, (spec, text) => (spec, text)).ToList();
		return AlignLoop(cols, filler, columnSeparator, rowSeparator);
	}
}
