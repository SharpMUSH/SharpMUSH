using System.Collections.Immutable;
using System.Globalization;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Layout;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// The smaller layout pieces: <c>gauge()</c>, <c>bullets()</c>, <c>grid()</c>, <c>datatable()</c>,
/// <c>datacolumns()</c>, <c>badge()</c> and <c>gradient()</c>. Like <c>box()</c>, each returns the text a
/// terminal shows with the layout riding on it.
/// </summary>
public partial class Functions
{
	/// <summary>gauge()'s options; its colours are read with the game's colour names.</summary>
	private static OptionSchema<Laid<Gauge>> GaugeOptions(Func<string, IColorMarkup?> color) => OptionSchema<Laid<Gauge>>.Empty
		.Width((laid, width) => laid with { Width = width })
		.NonEmptyText("filled", (laid, piece) => laid with { Block = laid.Block with { Filled = piece } })
		.NonEmptyText("empty", (laid, piece) => laid with { Block = laid.Block with { Empty = piece } })
		.Text("open", (laid, piece) => laid with { Block = laid.Block with { Open = piece } })
		.Text("close", (laid, piece) => laid with { Block = laid.Block with { Close = piece } })
		.Choice("show", Names<GaugeShow>(), (laid, show) => laid with { Block = laid.Block with { Show = show } })
		.Int("bar", 1, MaxLayoutWidth, (laid, bar) => laid with { Block = laid.Block with { BarWidth = bar } })
		.Gradient(color, laid => laid.Block.Gradient, (laid, gradient) => laid with { Block = laid.Block with { Gradient = gradient } })
		.Choice("shade", Names<GaugeShade>(), (laid, shade) => laid with { Block = laid.Block with { Shade = shade } });

	private static OptionSchema<Laid<Gauge>> GaugeSchema { get; } = GaugeOptions(_ => null);

	private static OptionSchema<Laid<Bullets>> BulletsSchema { get; } = InnerBorder(OptionSchema<Laid<Bullets>>.Empty)
		.Width((laid, width) => laid with { Width = width })
		.Choice("style", Names<BulletStyle>().Where(name => name.Value != BulletStyle.Custom).ToDictionary(),
			(laid, style) => laid with { Block = laid.Block with { Style = style } })
		.NonEmptyText("marker", (laid, marker) => laid with { Block = laid.Block with { Style = BulletStyle.Custom, Marker = marker } })
		.Int("start", 1, 100000, (laid, start) => laid with { Block = laid.Block with { Start = start } });

	private static OptionSchema<Laid<Grid>> GridSchema { get; } = InnerBorder(OptionSchema<Laid<Grid>>.Empty)
		.Width((laid, width) => laid with { Width = width })
		.Int("gap", 0, LayoutOptionGroups.MaxGap, (laid, gap) => laid with { Block = laid.Block with { Gap = gap } })
		.Flag("across", (laid, across) => laid with { Block = laid.Block with { Across = across } });

	/// <summary>
	/// A table's options. The per-column lists are kept as written: they are read once the delimiter
	/// (applied first) has split the headings into columns.
	/// </summary>
	private sealed record TableSettings(Table Table, MString Width, MString Delimiter, BorderStyle House)
	{
		public ImmutableList<(string Key, MString List)> ColumnLists { get; init; } = [];

		/// <summary>The border the options chose for the boxes and rules in the cells.</summary>
		public BorderStyle? Border { get; init; }
	}

	private static OptionSchema<TableSettings> DataTableSchema { get; } = OptionSchema<TableSettings>.Empty
		.Border(settings => settings.Border ?? settings.House, (settings, border) => settings with { Border = border })
		.Width((settings, width) => settings with { Width = width })
		.NonEmptyText("delim", (settings, delimiter) => settings with { Delimiter = delimiter })
		.Int("gap", 0, LayoutOptionGroups.MaxGap, (settings, gap) => settings with { Table = settings.Table with { Gap = gap } })
		.Text("sep", (settings, separator) => settings with { Table = settings.Table with { Separator = separator.Length == 0 ? null : separator } })
		.Text("rule", (settings, rule) => settings with { Table = settings.Table with { HeaderRule = rule } })
		.Text("priority", (settings, list) => settings with { ColumnLists = settings.ColumnLists.Add(("priority", list)) })
		.Text("min", (settings, list) => settings with { ColumnLists = settings.ColumnLists.Add(("min", list)) })
		.Text("max", (settings, list) => settings with { ColumnLists = settings.ColumnLists.Add(("max", list)) })
		.Text("nowrap", (settings, list) => settings with { ColumnLists = settings.ColumnLists.Add(("nowrap", list)) });

	/// <summary>A gradient's colours and the way it runs.</summary>
	private sealed record Shading(ColorGradient Gradient, GradientFlow Flow);

	private static OptionSchema<Shading> GradientSchema { get; } = OptionSchema<Shading>.Empty
		.GradientShape(shading => shading.Gradient, (shading, gradient) => shading with { Gradient = gradient })
		.Choice("flow", LayoutOptionGroups.Flows, (shading, flow) => shading with { Flow = flow });

	/// <summary>
	/// <c>gauge(&lt;value&gt;, &lt;maximum&gt;[, &lt;label&gt;[, &lt;options&gt;]])</c> — a bar filled to the
	/// value's share of the maximum, with the label before it and the figures after.
	/// </summary>
	[SharpFunction(Name = "gauge", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["value", "maximum", "label", "options"])]
	public ValueTask<CallState> Gauge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (!TryNumber(Arg(args, 0), out var value) || !TryNumber(Arg(args, 1), out var maximum))
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Numbers));
		if (maximum <= 0) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));

		var label = Arg(args, 2);
		var gauge = new Gauge(value, maximum) { Label = label.Length == 0 ? null : label };
		return ValueTask.FromResult(Laidout(parser, GaugeOptions(AnsiCodes), Arg(args, 3), gauge));
	}

	/// <summary>
	/// <c>bullets(&lt;list&gt;[, &lt;delimiter&gt;[, &lt;options&gt;]])</c> — each item of the list marked with a
	/// bullet or a number, a long item wrapping under its own text.
	/// </summary>
	[SharpFunction(Name = "bullets", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter", "options"])]
	public ValueTask<CallState> Bullets(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var items = ListItems(Arg(args, 0), Arg(args, 1));
		return ValueTask.FromResult(BulletsSchema.Apply(Arg(args, 2), new Laid<Bullets>(new Bullets([.. items.Select(item => Body(item))]), MarkupText.Empty, DefaultBorder())) switch
		{
			Laid<Bullets> laid => items.Length == 0 ? EmptyUnlessBadWidth(parser, laid.Width) : Finish(parser, laid.Block, laid.Width, laid.Border),
			Error<string> error => new CallState(error.Value),
		});
	}

	/// <summary>
	/// <c>grid(&lt;list&gt;[, &lt;delimiter&gt;[, &lt;options&gt;]])</c> — short items in as many columns as fit,
	/// down each column, or across each row with <c>across</c>.
	/// </summary>
	[SharpFunction(Name = "grid", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter", "options"])]
	public ValueTask<CallState> Grid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var items = ListItems(Arg(args, 0), Arg(args, 1));
		return ValueTask.FromResult(GridSchema.Apply(Arg(args, 2), new Laid<Grid>(new Grid([.. items]), MarkupText.Empty, DefaultBorder())) switch
		{
			Laid<Grid> laid => items.Length == 0 ? EmptyUnlessBadWidth(parser, laid.Width) : Finish(parser, laid.Block, laid.Width, laid.Border),
			Error<string> error => new CallState(error.Value),
		});
	}

	/// <summary>Nothing for an empty list, once its options have been checked as for a full one.</summary>
	private CallState EmptyUnlessBadWidth(IMUSHCodeParser parser, MString width) =>
		LayoutWidth(parser, width) is null ? new CallState(ErrorMessages.Returns.ArgRange) : CallState.Empty;

	/// <summary>
	/// <c>datatable(&lt;options&gt;, &lt;headings&gt;, &lt;row1&gt;[, ... &lt;rowN&gt;])</c> — rows under headings,
	/// cells split by <c>|</c>. Too wide, the columns that wrap give way, then the least important
	/// column is left out; when even one column does not fit, each row becomes labelled values.
	/// </summary>
	[SharpFunction(Name = "datatable", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["options", "headings", "row..."])]
	public ValueTask<CallState> DataTable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		return ValueTask.FromResult(BuildDataTable(parser, Arg(args, 0), delimiter =>
			(MushText.SplitList(delimiter, Arg(args, 1)), [.. Rest(args, 2).Select(row => MushText.SplitList(delimiter, row))])));
	}

	/// <summary>
	/// <c>datacolumns(&lt;options&gt;, &lt;column1&gt;[, ... &lt;columnN&gt;])</c> — <c>datatable()</c> given a
	/// column at a time: each column is its heading and then its cells, split by <c>|</c>, so a list
	/// a function returned is a column as it stands. A short column is filled out with empty cells.
	/// </summary>
	[SharpFunction(Name = "datacolumns", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["options", "column..."])]
	public ValueTask<CallState> DataColumns(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		return ValueTask.FromResult(BuildDataTable(parser, Arg(args, 0), delimiter =>
		{
			var columns = Rest(args, 1).Select(column => MushText.SplitList(delimiter, column)).ToArray();
			// A space-delimited column that is empty splits into nothing at all, not an empty heading.
			var height = Math.Max(0, columns.Max(column => column.Length) - 1);
			var rows = Enumerable.Range(1, height)
				.Select(r => columns.Select(column => r < column.Length ? column[r] : MarkupText.Empty).ToArray())
				.ToArray();
			return ([.. columns.Select(column => column.Length > 0 ? column[0] : MarkupText.Empty)], rows);
		}));
	}

	/// <summary>A table from its options and, once the delimiter is known, its headings and rows of cells.</summary>
	private CallState BuildDataTable(IMUSHCodeParser parser, MString options, Func<MString, (MString[] Headings, MString[][] Rows)> read) =>
		DataTableSchema.Apply(options, new TableSettings(new Table([], []), MarkupText.Empty, MarkupText.Plain("|"), DefaultBorder())) switch
		{
			TableSettings settings => BuildDataTable(parser, settings, read),
			Error<string> error => new CallState(error.Value),
		};

	private CallState BuildDataTable(IMUSHCodeParser parser, TableSettings settings, Func<MString, (MString[] Headings, MString[][] Rows)> read)
	{
		var (headings, cells) = read(settings.Delimiter);
		var columns = headings.Select(Heading).ToArray();
		foreach (var (key, list) in settings.ColumnLists)
		{
			if (ApplyColumnList(columns, key, MushText.SplitList(settings.Delimiter, list)) is { } failure) return new CallState(failure);
		}

		var rows = cells.Select(row => row.Select(cell => Body(cell)).ToImmutableArray()).ToImmutableArray();
		return Finish(parser, settings.Table with { Columns = [.. columns], Rows = rows }, settings.Width, settings.Border);
	}

	/// <summary>
	/// A per-column list applied to <paramref name="columns"/>: <c>priority</c>, <c>min</c> and <c>max</c>
	/// give a number for each column in turn (empty to leave one as it is), <c>nowrap</c> names columns
	/// by their place. The error, or null.
	/// </summary>
	private static string? ApplyColumnList(TableColumn[] columns, string key, MString[] list)
	{
		if (key == "nowrap")
		{
			foreach (var column in list)
			{
				if (!int.TryParse(column.ToPlainText().Trim(), out var index) || index < 1 || index > columns.Length) return ErrorMessages.Returns.ArgRange;
				columns[index - 1] = columns[index - 1] with { Wrap = false };
			}
			return null;
		}

		if (list.Length > columns.Length) return ErrorMessages.Returns.ArgRange;
		for (var c = 0; c < list.Length; c++)
		{
			var text = list[c].ToPlainText().Trim();
			if (text.Length == 0) continue;
			if (!int.TryParse(text, out var number)) return ErrorMessages.Returns.InvalidArgument;
			TableColumn? changed = key switch
			{
				"priority" when number is >= 1 and <= 99 => columns[c] with { Priority = number },
				"min" when number is >= 1 and <= MaxLayoutWidth => columns[c] with { Min = number },
				"max" when number is >= 0 and <= MaxLayoutWidth => columns[c] with { Max = number },
				_ => null,
			};
			if (changed is null) return ErrorMessages.Returns.ArgRange;
			columns[c] = changed;
		}
		return null;
	}

	/// <summary>A heading, after <c>align()</c>'s <c>&lt;</c>, <c>-</c> or <c>&gt;</c> to place its column's text.</summary>
	private static TableColumn Heading(MString heading) =>
		heading.Length > 1 && heading.Text[0] is '<' or '-' or '>' && LayoutOptionGroups.Alignments.TryGetValue(heading.Text[..1], out var placed)
			? new TableColumn(heading.Substring(1)) { Alignment = placed }
			: new TableColumn(heading);

	/// <summary>
	/// <c>badge(&lt;text&gt;[, &lt;kind&gt;])</c> — the text in brackets, coloured for its kind: <c>ok</c>,
	/// <c>warn</c>, <c>error</c>, <c>info</c> (the default) or <c>muted</c>.
	/// </summary>
	[SharpFunction(Name = "badge", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular, ParameterNames = ["text", "kind"])]
	public ValueTask<CallState> Badge(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var codes = Arg(args, 1).ToPlainText().Trim().ToLowerInvariant() switch
		{
			"" or "info" => "hc",
			"ok" => "hg",
			"warn" => "hy",
			"error" => "hr",
			"muted" => "hx",
			_ => null,
		};
		if (codes is null) return ValueTask.FromResult(new CallState("#-1 UNKNOWN BADGE KIND"));

		var text = MarkupText.Concat([MarkupText.Plain("["), Arg(args, 0), MarkupText.Plain("]")]);
		return ValueTask.FromResult(new CallState(MarkupText.Wrap(AnsiCodeParser.Parse(codes), text)));
	}

	/// <summary>
	/// <c>gradient(&lt;text&gt;, &lt;colors&gt;[, &lt;options&gt;])</c> — the text in colours blended one into the
	/// next, which are ansi() codes split by <c>|</c>, running along the characters, the words, across
	/// each line, down the lines or diagonally. Given a layout (a <c>box()</c>, a <c>datatable()</c>), the
	/// whole block is shaded, borders and all, and stays a block the portal draws.
	/// </summary>
	[SharpFunction(Name = "gradient", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["text", "colors", "options"])]
	public ValueTask<CallState> Gradient(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (LayoutOptionGroups.Stops(Arg(args, 1), AnsiCodes) is not { } stops) return ValueTask.FromResult(new CallState("#-1 UNKNOWN COLOR"));

		return ValueTask.FromResult(GradientSchema.Apply(Arg(args, 2), new Shading(new ColorGradient(stops), GradientFlow.Characters)) switch
		{
			Shading shading => new CallState(Shade(Arg(args, 0), shading)),
			Error<string> error => new CallState(error.Value),
		});
	}

	/// <summary>
	/// <paramref name="text"/> shaded: a layout as a <see cref="Shaded"/> block laid out again at its own
	/// width, any other text character by character.
	/// </summary>
	private static MString Shade(MString text, Shading shading) =>
		BlockLayout.AsBlock(text) is not TextBlock && LayoutOf(text) is { } layout
			? BlockLayout.Build(layout.Root.Shaded(shading.Gradient, shading.Flow), layout.Width, layout.Fluid)
			: shading.Gradient.Shade(text, shading.Flow);

	/// <summary>The layout that covers the whole of <paramref name="text"/>.</summary>
	private static LayoutMarkup? LayoutOf(MString text) =>
		text.Runs.IsDefaultOrEmpty ? null : text.Runs[0].Markups.OfType<LayoutMarkup>().FirstOrDefault();

	/// <summary>A list's items split on its delimiter (a space when none is given); none for an empty list.</summary>
	private static MString[] ListItems(MString list, MString delimiter) =>
		list.ToPlainText().Trim().Length == 0
			? []
			: MushText.SplitList(delimiter.Length == 0 ? MarkupText.Space : delimiter, list);

	private static bool TryNumber(MString arg, out double value) =>
		double.TryParse(arg.ToPlainText().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
