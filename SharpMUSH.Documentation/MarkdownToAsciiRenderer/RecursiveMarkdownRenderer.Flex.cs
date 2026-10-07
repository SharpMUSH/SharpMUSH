using SharpMUSH.Library.Services;
using MarkupString;
using MarkupString.Layout;
using Block = MarkupString.Layout.Block;
using System.Collections.Immutable;
using FlexAlign = SharpMUSH.Library.Services.FlexAlign;
using FlexJustify = SharpMUSH.Library.Services.FlexJustify;
using LayoutFlex = MarkupString.Layout.Flex;
using LayoutFlexAlign = MarkupString.Layout.FlexAlign;
using LayoutFlexJustify = MarkupString.Layout.FlexJustify;

namespace SharpMUSH.Documentation.MarkdownToAsciiRenderer;

public partial class RecursiveMarkdownRenderer
{
	/// <summary>
	/// A <c>flex</c> layout as a layout <see cref="LayoutFlex"/>: each item gets a width (a fixed <c>basis</c>
	/// first, then what is left shared by <c>grow</c>) and is rendered at that width; a column direction, or a
	/// row whose items would fall below their <c>min</c> width while wrapping is on, stacks the items instead,
	/// as the portal does on a phone. The terminal lines items up by the row's <c>align</c>; an item's own
	/// <c>align</c> is the portal's alone.
	/// </summary>
	protected virtual MString RenderFlex(FlexBlock flex)
	{
		var items = flex.OfType<FlexItemBlock>().ToList();
		if (items.Count == 0) return MarkupText.Empty;

		var options = flex.Options;
		if (options.Direction == FlexDirection.Column) return Laid(Column(items));

		var available = _maxWidth - options.Gap * (items.Count - 1);
		var widths = available > 0 ? ColumnWidths(items, available) : null;

		// Each item is rendered at the width the engine will give it (the same shares, worked out here), so
		// what it holds that the engine does not lay out itself, a code block, fits as it is.
		var sized = items.Select((item, i) => (Block)new Sized(ItemBody(widths is null ? RenderFlexItem(item) : RenderFlexItem(item, widths[i])))
		{
			Basis = item.Options.Basis is not null && widths is not null ? BlockSize.Cells(widths[i]) : BlockSize.Auto,
			Grow = item.Options.Basis is null ? item.Options.Grow : 0,
			Min = options.Wrap ? Math.Min(item.Options.Min, _maxWidth) : 1
		}).ToImmutableArray();

		return Laid(new LayoutFlex(sized)
		{
			Gap = options.Gap,
			Align = options.Align switch
			{
				FlexAlign.Center => LayoutFlexAlign.Center,
				FlexAlign.End => LayoutFlexAlign.End,
				_ => LayoutFlexAlign.Start
			},
			Justify = options.Justify switch
			{
				FlexJustify.End => LayoutFlexJustify.End,
				FlexJustify.Center => LayoutFlexJustify.Center,
				FlexJustify.Between => LayoutFlexJustify.Between,
				_ => LayoutFlexJustify.Start
			}
		});
	}

	/// <summary>
	/// A column direction's items one under the other with a blank line between them, each at its basis
	/// when it has one, as the portal keeps a basis as a width in a column.
	/// </summary>
	private Stack Column(IReadOnlyList<FlexItemBlock> items)
	{
		var stacked = new List<Block>();
		foreach (var item in items)
		{
			var body = ItemBody(RenderFlexItem(item));
			if (body is TextBlock { Content.Length: 0 }) continue;
			if (stacked.Count > 0) stacked.Add(new TextBlock(MarkupText.Empty));
			stacked.Add(item.Options.Basis is { } basis
				? new LayoutFlex([new Sized(body) { Basis = basis.IsPercent ? BlockSize.Percent(basis.Value) : BlockSize.Cells(basis.Value), Grow = 0 }])
				: body);
		}
		return new Stack([.. stacked]);
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

	/// <summary>An item's blocks, rendered to <paramref name="width"/>.</summary>
	private MString RenderFlexItem(FlexItemBlock item, int width)
	{
		var outer = _maxWidth;
		_maxWidth = width;
		try
		{
			return RenderFlexItem(item);
		}
		finally
		{
			_maxWidth = outer;
		}
	}
}
