using SharpMUSH.Library.Services;
using MarkupString;
using System.Text.RegularExpressions;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	/// <summary>
	/// A <c>flex</c> layout as columns, the way <c>align()</c> sets them: each item gets a width (a fixed
	/// <c>basis</c> first, then what is left shared by <c>grow</c>), renders at that width, and the columns are
	/// joined line by line with the gap between them. A column direction, or a row whose items would fall below
	/// their <c>min</c> width while wrapping is on, stacks the items instead, as the portal does on a phone.
	/// </summary>
	protected virtual MString RenderFlex(FlexBlock flex)
	{
		var items = flex.OfType<FlexItemBlock>().ToList();
		if (items.Count == 0) return MarkupText.Empty;

		var options = flex.Options;
		var available = _maxWidth - options.Gap * (items.Count - 1);
		var widths = available > 0 ? ColumnWidths(items, available) : null;
		var fits = widths is not null && items.Select((item, i) => widths[i] >= Math.Min(item.Options.Min, _maxWidth)).All(ok => ok);

		if (options.Direction == FlexDirection.Column || widths is null || (options.Wrap && !fits))
		{
			var stacked = items.Select(RenderFlexItem).Where(IsNonWhitespace).ToList();
			return MarkupText.Join(MarkupText.Plain("\n\n"), stacked);
		}

		var columns = items.Select((item, i) => FlexColumn(item, widths[i])).ToList();
		var height = columns.Max(column => column.Count);
		var aligned = columns.Select((column, i) =>
			AlignColumn(column, widths[i], height, items[i].Options.Align ?? options.Align)).ToList();

		var spare = available - widths.Sum();
		var (lead, between) = options.Justify switch
		{
			FlexJustify.End => (spare, 0),
			FlexJustify.Center => (spare / 2, 0),
			FlexJustify.Between when items.Count > 1 => (0, spare / (items.Count - 1)),
			_ => (0, 0)
		};
		var gap = MarkupText.Plain(new string(' ', options.Gap + between));
		var indent = MarkupText.Plain(new string(' ', lead));

		var lines = Enumerable.Range(0, height).Select(row =>
		{
			var parts = new List<MString> { indent };
			for (var i = 0; i < aligned.Count; i++)
			{
				if (i > 0) parts.Add(gap);
				parts.Add(aligned[i][row]);
			}

			return MarkupText.Concat(parts).Trim(TrimType.TrimEnd, " ");
		});

		return MarkupText.Join(MarkupText.NewLine, lines.ToArray());
	}

	/// <summary>
	/// Each item's width in columns out of <paramref name="available"/>: a fixed basis as given, then the rest
	/// shared by grow, any remainder going to the first growing items. Null when the fixed widths alone overrun.
	/// </summary>
	private static int[]? ColumnWidths(IReadOnlyList<FlexItemBlock> items, int available)
	{
		var widths = items.Select(item => item.Options.Basis?.Columns(available) ?? 0).ToArray();
		var growing = Enumerable.Range(0, items.Count).Where(i => items[i].Options.Basis is null).ToList();
		var rest = available - widths.Sum();
		if (rest < 0 || (growing.Count > 0 && rest < growing.Count)) return null;
		if (growing.Count == 0) return widths;

		var totalGrow = growing.Sum(i => items[i].Options.Grow);
		foreach (var i in growing)
		{
			widths[i] = rest * items[i].Options.Grow / totalGrow;
		}

		var remainder = rest - growing.Sum(i => widths[i]);
		for (var k = 0; remainder > 0; k = (k + 1) % growing.Count, remainder--)
		{
			widths[growing[k]]++;
		}

		return widths;
	}

	/// <summary>An item's blocks, rendered to the width currently in force.</summary>
	private MString RenderFlexItem(FlexItemBlock item)
	{
		var parts = item.Select(child => Render(child)).Where(IsNonWhitespace).ToList();
		return MarkupText.Join(MarkupText.Plain("\n"), parts);
	}

	/// <summary>An item rendered at <paramref name="width"/> and broken into lines no wider than it.</summary>
	private List<MString> FlexColumn(FlexItemBlock item, int width)
	{
		var outer = _maxWidth;
		_maxWidth = width;
		try
		{
			var rendered = RenderFlexItem(item);
			return rendered.Length == 0
				? []
				: rendered.Split("\n").SelectMany(line => WrapInColumn(line, width)).ToList();
		}
		finally
		{
			_maxWidth = outer;
		}
	}

	/// <summary>
	/// A line broken to <paramref name="width"/>. A list item's continuation lines hang under its text, not its
	/// marker, as they would in a browser.
	/// </summary>
	private static IEnumerable<MString> WrapInColumn(MString line, int width)
	{
		if (line.Length == 0) return [line];

		var marker = ListMarkerPrefix().Match(line.ToPlainText());
		var hang = marker.Success ? marker.Length : 0;
		if (hang == 0 || hang >= width - 4) return line.WrapLines(width);

		var pieces = line.Substring(hang, line.Length - hang).WrapLines(width - hang).ToList();
		var indent = MarkupText.Plain(new string(' ', hang));
		return pieces.Select((piece, i) => MarkupText.Concat(i == 0 ? line.Substring(0, hang) : indent, piece));
	}

	[GeneratedRegex(@"^\s*(?:\*|\d+\.) ", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
	private static partial Regex ListMarkerPrefix();

	/// <summary>A column's lines each filled to <paramref name="width"/>, with blank lines placing it in <paramref name="height"/>.</summary>
	private static List<MString> AlignColumn(List<MString> lines, int width, int height, FlexAlign align)
	{
		var blank = MarkupText.Plain(new string(' ', width));
		var filled = lines.Select(line => line.Pad(MarkupText.Space, width, PadType.Right, TruncationType.Truncate)).ToList();
		var spare = height - filled.Count;
		var above = align switch
		{
			FlexAlign.End => spare,
			FlexAlign.Center => spare / 2,
			_ => 0
		};

		return [.. Enumerable.Repeat(blank, above), .. filled, .. Enumerable.Repeat(blank, spare - above)];
	}
}
