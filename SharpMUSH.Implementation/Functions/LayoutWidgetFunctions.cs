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
/// The smaller layout pieces: <c>gauge()</c>, <c>bullets()</c>, <c>grid()</c>, <c>datatable()</c> and
/// <c>badge()</c>. Like <c>box()</c>, each returns the text a terminal shows with the layout riding on it.
/// </summary>
public partial class Functions
{
	private static readonly IReadOnlySet<string> GaugeKeys =
		new HashSet<string>(["width", "filled", "empty", "open", "close", "show", "bar"], StringComparer.OrdinalIgnoreCase);

	private static readonly IReadOnlySet<string> BulletsKeys =
		new HashSet<string>(["width", "style", "marker", "start"], StringComparer.OrdinalIgnoreCase);

	private static readonly IReadOnlySet<string> GridKeys =
		new HashSet<string>(["width", "gap", "across"], StringComparer.OrdinalIgnoreCase);

	private static readonly IReadOnlySet<string> DataTableKeys =
		new HashSet<string>(["width", "delim", "gap", "sep", "rule", "priority", "min", "max", "nowrap"], StringComparer.OrdinalIgnoreCase);

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

		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 3), GaugeKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildGauge(parser, value, maximum, Arg(args, 2), options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildGauge(IMUSHCodeParser parser, double value, double maximum, MString label, IReadOnlyList<(string Key, MString Value)> options)
	{
		var settings = GaugeOptions.Default;
		var widthArg = MarkupText.Empty;
		foreach (var (key, option) in options)
		{
			var plain = option.ToPlainText().Trim().ToLowerInvariant();
			switch (key)
			{
				case "width":
					widthArg = option;
					break;
				case "filled" or "empty" when option.Length == 0:
					return new CallState(ErrorMessages.Returns.InvalidArgument);
				case "filled":
					settings = settings with { Filled = option };
					break;
				case "empty":
					settings = settings with { Empty = option };
					break;
				case "open":
					settings = settings with { Open = option };
					break;
				case "close":
					settings = settings with { Close = option };
					break;
				case "show" when plain is "percent" or "value" or "none":
					settings = settings with { Show = plain switch { "value" => GaugeShow.Value, "none" => GaugeShow.None, _ => GaugeShow.Percent } };
					break;
				case "bar" when int.TryParse(plain, out var bar) && bar is > 0 and <= MaxLayoutWidth:
					settings = settings with { BarWidth = bar };
					break;
				default:
					return new CallState(ErrorMessages.Returns.InvalidArgument);
			}
		}

		if (LayoutWidth(parser, widthArg) is not (int width, bool fluid)) return new CallState(ErrorMessages.Returns.ArgRange);
		return new CallState(BlockLayout.Build(new GaugeNode(value, maximum, label.Length == 0 ? null : label, settings), width, fluid));
	}

	/// <summary>
	/// <c>bullets(&lt;list&gt;[, &lt;delimiter&gt;[, &lt;options&gt;]])</c> — each item of the list marked with a
	/// bullet or a number, a long item wrapping under its own text.
	/// </summary>
	[SharpFunction(Name = "bullets", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter", "options"])]
	public ValueTask<CallState> Bullets(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 2), BulletsKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildBullets(parser, ListItems(Arg(args, 0), Arg(args, 1)), options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildBullets(IMUSHCodeParser parser, MString[] items, IReadOnlyList<(string Key, MString Value)> options)
	{
		var settings = BulletOptions.Default;
		var widthArg = MarkupText.Empty;
		foreach (var (key, option) in options)
		{
			var plain = option.ToPlainText().Trim().ToLowerInvariant();
			switch (key)
			{
				case "width":
					widthArg = option;
					break;
				case "style" when plain is not "custom" && Enum.TryParse<BulletStyle>(plain, ignoreCase: true, out var style) && Enum.IsDefined(style):
					settings = settings with { Style = style };
					break;
				case "marker" when option.Length > 0:
					settings = settings with { Style = BulletStyle.Custom, Marker = option };
					break;
				case "start" when int.TryParse(plain, out var start) && start is >= 1 and <= 100000:
					settings = settings with { Start = start };
					break;
				default:
					return new CallState(ErrorMessages.Returns.InvalidArgument);
			}
		}

		if (LayoutWidth(parser, widthArg) is not (int width, bool fluid)) return new CallState(ErrorMessages.Returns.ArgRange);
		if (items.Length == 0) return CallState.Empty;
		return new CallState(BlockLayout.Build(new BulletsNode([.. items.Select(item => Body(item))], settings), width, fluid));
	}

	/// <summary>
	/// <c>grid(&lt;list&gt;[, &lt;delimiter&gt;[, &lt;options&gt;]])</c> — short items in as many columns as fit,
	/// down each column, or across each row with <c>across</c>.
	/// </summary>
	[SharpFunction(Name = "grid", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["list", "delimiter", "options"])]
	public ValueTask<CallState> Grid(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 2), GridKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildGrid(parser, ListItems(Arg(args, 0), Arg(args, 1)), options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildGrid(IMUSHCodeParser parser, MString[] items, IReadOnlyList<(string Key, MString Value)> options)
	{
		var gap = 2;
		var across = false;
		var widthArg = MarkupText.Empty;
		foreach (var (key, option) in options)
		{
			switch (key)
			{
				case "width":
					widthArg = option;
					break;
				case "gap" when int.TryParse(option.ToPlainText().Trim(), out var cells) && cells is >= 0 and <= 20:
					gap = cells;
					break;
				case "across":
					across = LayoutSpec.IsYes(option);
					break;
				default:
					return new CallState(ErrorMessages.Returns.InvalidArgument);
			}
		}

		if (LayoutWidth(parser, widthArg) is not (int width, bool fluid)) return new CallState(ErrorMessages.Returns.ArgRange);
		if (items.Length == 0) return CallState.Empty;
		return new CallState(BlockLayout.Build(new GridNode([.. items], gap, across), width, fluid));
	}

	/// <summary>
	/// <c>datatable(&lt;options&gt;, &lt;headings&gt;, &lt;row1&gt;[, ... &lt;rowN&gt;])</c> — rows under headings,
	/// cells split by <c>|</c>. Too wide, the columns that wrap give way, then the least important
	/// column is left out; when even one column does not fit, each row becomes labelled values.
	/// </summary>
	[SharpFunction(Name = "datatable", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["options", "headings", "row..."])]
	public ValueTask<CallState> DataTable(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 0), DataTableKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildDataTable(parser, args, options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildDataTable(IMUSHCodeParser parser, IReadOnlyDictionary<string, CallState> args, IReadOnlyList<(string Key, MString Value)> options)
	{
		var delimiter = options.LastOrDefault(option => option.Key == "delim").Value is { Length: > 0 } given ? given : MarkupText.Plain("|");
		var headings = MushText.SplitList(delimiter, Arg(args, 1));
		var columns = headings.Select(Heading).ToArray();

		var settings = TableOptions.Default;
		var widthArg = MarkupText.Empty;
		foreach (var (key, option) in options)
		{
			switch (key)
			{
				case "width":
					widthArg = option;
					break;
				case "delim":
					break;
				case "gap" when int.TryParse(option.ToPlainText().Trim(), out var gap) && gap is >= 0 and <= 20:
					settings = settings with { Gap = gap };
					break;
				case "sep":
					settings = settings with { Separator = option.Length == 0 ? null : option };
					break;
				case "rule":
					settings = settings with { HeaderRule = option };
					break;
				case "priority" or "min" or "max":
					var numbers = MushText.SplitList(delimiter, option);
					if (numbers.Length > columns.Length) return new CallState(ErrorMessages.Returns.ArgRange);
					for (var c = 0; c < numbers.Length; c++)
					{
						var text = numbers[c].ToPlainText().Trim();
						if (text.Length == 0) continue;
						if (!int.TryParse(text, out var number)) return new CallState(ErrorMessages.Returns.InvalidArgument);
						TableColumn? changed = key switch
						{
							"priority" when number is >= 1 and <= 99 => columns[c] with { Priority = number },
							"min" when number is >= 1 and <= MaxLayoutWidth => columns[c] with { Min = number },
							"max" when number is >= 0 and <= MaxLayoutWidth => columns[c] with { Max = number },
							_ => null,
						};
						if (changed is null) return new CallState(ErrorMessages.Returns.ArgRange);
						columns[c] = changed;
					}
					break;
				case "nowrap":
					foreach (var column in MushText.SplitList(delimiter, option))
					{
						if (!int.TryParse(column.ToPlainText().Trim(), out var index) || index < 1 || index > columns.Length)
							return new CallState(ErrorMessages.Returns.ArgRange);
						columns[index - 1] = columns[index - 1] with { Wrap = false };
					}
					break;
				default:
					return new CallState(ErrorMessages.Returns.InvalidArgument);
			}
		}

		if (LayoutWidth(parser, widthArg) is not (int width, bool fluid)) return new CallState(ErrorMessages.Returns.ArgRange);

		var rows = args.Keys.Select(int.Parse).Where(i => i > 1).Order()
			.Select(i => MushText.SplitList(delimiter, args[i.ToString()].Message!).Select(cell => Body(cell)).ToImmutableArray())
			.ToImmutableArray();
		return new CallState(BlockLayout.Build(new TableNode([.. columns], rows, settings), width, fluid));
	}

	/// <summary>A heading, after <c>align()</c>'s <c>&lt;</c>, <c>-</c> or <c>&gt;</c> to place its column's text.</summary>
	private static TableColumn Heading(MString heading)
	{
		var alignment = heading.Length > 1 ? LayoutSpec.ParseAlignment(heading.Substring(0, 1)) : null;
		return alignment is { } placed
			? new TableColumn(heading.Substring(1), placed)
			: new TableColumn(heading);
	}

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

	/// <summary>A list's items split on its delimiter (a space when none is given); none for an empty list.</summary>
	private static MString[] ListItems(MString list, MString delimiter) =>
		list.ToPlainText().Trim().Length == 0
			? []
			: MushText.SplitList(delimiter.Length == 0 ? MarkupText.Space : delimiter, list);

	private static bool TryNumber(MString arg, out double value) =>
		double.TryParse(arg.ToPlainText().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
