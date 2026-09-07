using MarkupString;
using MarkupString.Layout;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// The column layout behind <c>align()</c> and <c>lalign()</c>.
/// </summary>
/// <remarks>
/// The layout itself lives in <see cref="TextLayout"/>; all this does is translate PennMUSH's
/// column specification into the engine's named options. That translation is the whole job on
/// purpose — the engine speaks behaviour rather than flag characters, because PennMUSH and
/// RhostMUSH use the same characters for different and sometimes opposite things, so mapping
/// one server's syntax is the caller's business.
/// </remarks>
public static class TextAligner
{
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

		var cells = new List<LayoutCell>(columnSpecs.Count * 2 - 1);
		for (var i = 0; i < columnSpecs.Count; i++)
		{
			if (i > 0) cells.Add(new LayoutSeparator(columnSeparator));
			cells.Add(new LayoutColumn(colList[i], Format(columnSpecs[i], filler)));
		}

		return TextLayout.Render(
			System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cells),
			new LayoutOptions { RowSeparator = rowSeparator });
	}

	/// <summary>One PennMUSH column specification as the engine's options.</summary>
	private static ColumnFormat Format(ColumnSpec spec, MarkupText filler) => new()
	{
		Width = spec.Width,
		Fill = filler,

		// PennMUSH columns wrap on words, and its 'x' and 'X' options replace that with a cut at
		// each row of the column's own text: 'x' keeps every row, 'X' only the first.
		Wrap = spec.Options.HasFlag(ColumnOptions.Truncate) || spec.Options.HasFlag(ColumnOptions.TruncateV2)
			? WrapMode.HardBreaks
			: WrapMode.Word,
		MaxLines = spec.Options.HasFlag(ColumnOptions.TruncateV2) ? 1 : 0,

		Alignment = spec.Justification switch
		{
			Justification.Left => Alignment.Left,
			Justification.Right => Alignment.Right,
			Justification.Center => Alignment.Center,
			Justification.Full => Alignment.Full,
			Justification.Paragraph => Alignment.Paragraph,
			_ => Alignment.Left,
		},

		// PennMUSH merges rather than shifts: an exhausted column hands its cells to a neighbour,
		// which widens in place and goes on wrapping into the room.
		WhenEmpty = spec.Options.HasFlag(ColumnOptions.MergeToLeft) ? WhenEmpty.GiveSpaceToLeft
			: spec.Options.HasFlag(ColumnOptions.MergeToRight) ? WhenEmpty.GiveSpaceToRight
			: WhenEmpty.None,

		Repeat = spec.Options.HasFlag(ColumnOptions.Repeat),
		NoFill = spec.Options.HasFlag(ColumnOptions.NoFill),
		NoSeparatorAfter = spec.Options.HasFlag(ColumnOptions.NoColSep),

		// PennMUSH's (ansi) column option. The engine applies it to the whole drawn line, filler
		// included, which is what a coloured column means.
		Markup = string.IsNullOrEmpty(spec.Ansi) ? null : AnsiCodeParser.Parse(spec.Ansi),
	};

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
}
