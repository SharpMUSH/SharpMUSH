using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// SharpMUSH's own layout functions: <c>box()</c>, <c>rule()</c>, <c>flex()</c>, <c>item()</c>,
/// <c>figure()</c>, <c>fields()</c>, <c>tree()</c> and <c>node()</c>. Each returns the text a terminal
/// shows — the same box art <c>align()</c> and <c>repeat()</c> would draw — with the layout it was
/// drawn from riding on it, so the portal draws a bordered card whose columns wrap on a phone, and a
/// telnet client is sent the box again at its own width. <c>align()</c>, <c>center()</c> and the rest are unchanged.
/// </summary>
public partial class Functions
{
	private static readonly IReadOnlySet<string> BoxKeys =
		new HashSet<string>(LayoutSpec.BorderKeys.Append("title").Append("pad"), StringComparer.OrdinalIgnoreCase);

	private static readonly IReadOnlySet<string> RuleKeys =
		new HashSet<string>(LayoutSpec.BorderKeys.Append("title"), StringComparer.OrdinalIgnoreCase);

	private static readonly IReadOnlySet<string> FlexKeys =
		new HashSet<string>(["width", "gap", "sep", "justify", "align", "vertical"], StringComparer.OrdinalIgnoreCase);

	private static readonly IReadOnlySet<string> FieldsKeys =
		new HashSet<string>(["width", "align", "sep", "leader", "cols", "gap"], StringComparer.OrdinalIgnoreCase);

	private static readonly IReadOnlySet<string> TreeKeys =
		new HashSet<string>(["width", "guide", "branch", "last", "pipe", "blank"], StringComparer.OrdinalIgnoreCase);

	/// <summary>The widest layout a function will draw.</summary>
	private const int MaxLayoutWidth = 1000;

	/// <summary>
	/// <c>box(&lt;body&gt;[, &lt;title&gt;[, &lt;width&gt;[, &lt;options&gt;]]])</c> — a frame round the body with
	/// the title set into its top edge. A <c>rule()</c> on a line of its own in the body becomes a
	/// divider meeting the sides; a <c>flex()</c> becomes columns.
	/// </summary>
	[SharpFunction(Name = "box", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["body", "title", "width", "options"])]
	public ValueTask<CallState> Box(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (LayoutWidth(parser, Arg(args, 2)) is not (int width, bool fluid)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));

		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 3), BoxKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildBox(Arg(args, 0), Arg(args, 1), width, fluid, options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildBox(MString body, MString title, int width, bool fluid, IReadOnlyList<(string Key, MString Value)> options)
	{
		if (LayoutSpec.Border(options, DefaultBorder()) is not BorderStyle border) return new CallState("#-1 UNKNOWN BORDER STYLE");

		var titleAlignment = Alignment.Center;
		var padding = 1;
		foreach (var (key, value) in options)
		{
			switch (key)
			{
				case "title" when LayoutSpec.ParseAlignment(value) is { } aligned:
					titleAlignment = aligned;
					break;
				case "title":
					return new CallState(ErrorMessages.Returns.InvalidArgument);
				case "pad" when int.TryParse(value.ToPlainText(), out var pad) && pad is >= 0 and <= 10:
					padding = pad;
					break;
				case "pad":
					return new CallState(ErrorMessages.Returns.ArgRange);
			}
		}

		var node = new BoxNode(Body(body), border, title.Length == 0 ? null : title, titleAlignment, padding);
		return new CallState(BlockLayout.Build(node, width, fluid));
	}

	/// <summary>
	/// <c>rule([&lt;title&gt;[, &lt;width&gt;[, &lt;options&gt;]]])</c> — a line across the width with the
	/// title set into it. Inside a <c>box()</c> it divides the box.
	/// </summary>
	[SharpFunction(Name = "rule", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["title", "width", "options"])]
	public ValueTask<CallState> Rule(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (LayoutWidth(parser, Arg(args, 1)) is not (int width, bool fluid)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));

		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 2), RuleKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildRule(Arg(args, 0), width, fluid, options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildRule(MString title, int width, bool fluid, IReadOnlyList<(string Key, MString Value)> options)
	{
		if (LayoutSpec.Border(options, DefaultBorder()) is not BorderStyle border) return new CallState("#-1 UNKNOWN BORDER STYLE");

		var titleAlignment = Alignment.Center;
		foreach (var (key, value) in options)
		{
			if (key != "title") continue;
			if (LayoutSpec.ParseAlignment(value) is not { } aligned) return new CallState(ErrorMessages.Returns.InvalidArgument);
			titleAlignment = aligned;
		}

		return new CallState(BlockLayout.Build(new RuleNode(title.Length == 0 ? null : title, border, titleAlignment), width, fluid));
	}

	/// <summary>
	/// <c>flex([&lt;options&gt;], &lt;item1&gt;[, ... &lt;itemN&gt;])</c> — the items side by side, sharing the
	/// width, stacked when they do not fit. An item is text, a block, or an <c>item()</c> that says how
	/// wide it wants to be.
	/// </summary>
	[SharpFunction(Name = "flex", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["options", "item..."])]
	public ValueTask<CallState> Flex(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 0), FlexKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildFlex(parser, args, options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildFlex(IMUSHCodeParser parser, IReadOnlyDictionary<string, CallState> args, IReadOnlyList<(string Key, MString Value)> options)
	{
		var flexOptions = FlexOptions.Default;
		var widthArg = MarkupText.Empty;
		foreach (var (key, value) in options)
		{
			var plain = value.ToPlainText().Trim().ToLowerInvariant();
			switch (key)
			{
				case "width":
					widthArg = value;
					break;
				case "gap" when int.TryParse(plain, out var gap) && gap is >= 0 and <= 20:
					flexOptions = flexOptions with { Gap = gap };
					break;
				case "sep":
					flexOptions = flexOptions with { Separator = value };
					break;
				case "justify" when Enum.TryParse<FlexJustify>(plain, ignoreCase: true, out var justify) && Enum.IsDefined(justify):
					flexOptions = flexOptions with { Justify = justify };
					break;
				case "align" when plain is "top" or "center" or "centre" or "bottom":
					flexOptions = flexOptions with { Align = plain switch { "top" => FlexAlign.Start, "bottom" => FlexAlign.End, _ => FlexAlign.Center } };
					break;
				case "vertical":
					flexOptions = flexOptions with { Vertical = LayoutSpec.IsYes(value) };
					break;
				default:
					return new CallState(ErrorMessages.Returns.InvalidArgument);
			}
		}

		if (LayoutWidth(parser, widthArg) is not (int width, bool fluid)) return new CallState(ErrorMessages.Returns.ArgRange);

		var items = args.Keys.Select(int.Parse).Where(i => i > 0).Order()
			.Select(i => FlexItemOf(args[i.ToString()].Message!))
			.ToArray();
		return new CallState(BlockLayout.Build(new FlexNode([.. items], flexOptions), width, fluid));
	}

	/// <summary>
	/// <c>item(&lt;content&gt;[, &lt;width&gt;[, &lt;min&gt;[, &lt;grow&gt;]]])</c> — one item of a <c>flex()</c>:
	/// <c>auto</c>, a number of cells or a percentage, after <c>align()</c>'s <c>&lt;</c>, <c>-</c> or
	/// <c>&gt;</c> to place the text; the fewest cells it can be drawn in before the items stack; and its
	/// share of any spare width.
	/// </summary>
	[SharpFunction(Name = "item", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["content", "width", "min", "grow"])]
	public ValueTask<CallState> Item(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (!LayoutSpec.TryItemWidth(Arg(args, 1).ToPlainText(), out var basis, out var alignment)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidArgument));
		if (!TryCount(Arg(args, 2), 1, out var min) || !TryCount(Arg(args, 3), 0, out var grow)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));
		if (LayoutWidth(parser, MarkupText.Empty) is not (int width, bool fluid)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));

		var item = new FlexItem(Body(Arg(args, 0), alignment), basis, Math.Max(1, min), grow);
		return ValueTask.FromResult(new CallState(BlockLayout.Build(new FlexNode([item], FlexOptions.Default), width, fluid)));
	}

	/// <summary>
	/// <c>figure(&lt;address&gt;[, &lt;description&gt;[, &lt;art&gt;[, &lt;float&gt;[, &lt;beside&gt;[, &lt;width&gt;]]]]])</c>
	/// — a picture with the text art a terminal shows instead of it, and, floated <c>left</c> or
	/// <c>right</c>, text that flows round it. The picture is shown only when the caller may send
	/// pictures (as <c>image()</c>) and <c>image_hosts</c> allows its host; otherwise the art is.
	/// </summary>
	[SharpFunction(Name = "figure", MinArgs = 1, MaxArgs = 6, Flags = FunctionFlags.Regular, ParameterNames = ["address", "description", "art", "float", "beside", "width"])]
	public async ValueTask<CallState> Figure(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var address = Arg(args, 0).ToPlainText().Trim();
		if (address.Any(char.IsControl)) return new CallState(ErrorMessages.Returns.InvalidArgument);

		var side = Arg(args, 3).ToPlainText().Trim().ToLowerInvariant() switch
		{
			"" or "none" => (FigureFloat?)FigureFloat.None,
			"left" => FigureFloat.Left,
			"right" => FigureFloat.Right,
			_ => null,
		};
		if (side is not { } floated) return new CallState(ErrorMessages.Returns.InvalidArgument);
		if (LayoutWidth(parser, Arg(args, 5)) is not (int width, bool fluid)) return new CallState(ErrorMessages.Returns.ArgRange);

		var shown = address.Length > 0
			&& await CanSendOob(await parser.CurrentState.KnownExecutorObject(Mediator))
			&& ImageAllowed(address);
		var description = Arg(args, 1).ToPlainText();
		var image = new ImageMarkup(shown ? address : string.Empty, description.Length == 0 ? null : description);
		var beside = Arg(args, 4);

		var node = new FigureNode(image, Arg(args, 2), floated, beside.Length == 0 ? null : Body(beside));
		return new CallState(BlockLayout.Build(node, width, fluid));
	}

	/// <summary>
	/// <c>fields([&lt;options&gt;], &lt;label1&gt;, &lt;value1&gt;[, ... &lt;labelN&gt;, &lt;valueN&gt;])</c> — labelled
	/// values with the values lined up in one column, a long value wrapping under itself.
	/// </summary>
	[SharpFunction(Name = "fields", MinArgs = 3, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["options", "label...", "value..."])]
	public ValueTask<CallState> Fields(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (args.Count % 2 == 0) return ValueTask.FromResult(new CallState(string.Format(ErrorMessages.Returns.GotUnEvenArgs, "FIELDS")));

		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 0), FieldsKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildFields(parser, args, options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildFields(IMUSHCodeParser parser, IReadOnlyDictionary<string, CallState> args, IReadOnlyList<(string Key, MString Value)> options)
	{
		var settings = FieldsOptions.Default;
		var widthArg = MarkupText.Empty;
		foreach (var (key, value) in options)
		{
			var plain = value.ToPlainText().Trim().ToLowerInvariant();
			switch (key)
			{
				case "width":
					widthArg = value;
					break;
				case "align" when plain is "left" or "right":
					settings = settings with { LabelAlignment = plain == "right" ? Alignment.Right : Alignment.Left };
					break;
				case "sep":
					settings = settings with { Separator = value };
					break;
				case "leader":
					settings = settings with { Leader = value.Length == 0 ? null : value };
					break;
				case "cols" when int.TryParse(plain, out var columns) && columns is >= 1 and <= 10:
					settings = settings with { Columns = columns };
					break;
				case "gap" when int.TryParse(plain, out var gap) && gap is >= 0 and <= 20:
					settings = settings with { Gap = gap };
					break;
				default:
					return new CallState(ErrorMessages.Returns.InvalidArgument);
			}
		}

		if (LayoutWidth(parser, widthArg) is not (int width, bool fluid)) return new CallState(ErrorMessages.Returns.ArgRange);

		var fields = new List<Field>();
		for (var i = 1; i + 1 < args.Count; i += 2)
			fields.Add(new Field(Arg(args, i), Body(Arg(args, i + 1))));
		return new CallState(BlockLayout.Build(new FieldsNode([.. fields], settings), width, fluid));
	}

	/// <summary>
	/// <c>tree([&lt;options&gt;], &lt;item1&gt;[, ... &lt;itemN&gt;])</c> — items with the items under them,
	/// joined by guide lines. An item is text, or a <c>node()</c> that has items of its own.
	/// </summary>
	[SharpFunction(Name = "tree", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["options", "item..."])]
	public ValueTask<CallState> Tree(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		return ValueTask.FromResult(LayoutSpec.Options(Arg(args, 0), TreeKeys) switch
		{
			IReadOnlyList<(string Key, MString Value)> options => BuildTree(parser, args, options),
			Error<string> error => new CallState(error.Value),
		});
	}

	private CallState BuildTree(IMUSHCodeParser parser, IReadOnlyDictionary<string, CallState> args, IReadOnlyList<(string Key, MString Value)> options)
	{
		var guide = TreeGuide.Line;
		var widthArg = MarkupText.Empty;
		foreach (var (key, value) in options)
		{
			if (key == "width") widthArg = value;
			else if (key == "guide")
			{
				if (TreeGuide.Preset(value.ToPlainText().Trim()) is not { } preset) return new CallState("#-1 UNKNOWN GUIDE STYLE");
				guide = preset;
			}
		}

		// Each piece replaces its part, padded or cut to the width of the branch so the levels line up.
		foreach (var (key, value) in options)
		{
			guide = key switch
			{
				"branch" => guide with { Branch = value },
				"last" => guide with { Last = value },
				"pipe" => guide with { Pipe = value },
				"blank" => guide with { Blank = value },
				_ => guide,
			};
		}
		var step = Math.Max(guide.Branch.DisplayWidth, guide.Last.DisplayWidth);
		guide = guide with
		{
			Branch = Step(guide.Branch, step),
			Last = Step(guide.Last, step),
			Pipe = Step(guide.Pipe, step),
			Blank = Step(guide.Blank, step),
		};

		if (LayoutWidth(parser, widthArg) is not (int width, bool fluid)) return new CallState(ErrorMessages.Returns.ArgRange);

		var items = args.Keys.Select(int.Parse).Where(i => i > 0).Order()
			.SelectMany(i => TreeItemsOf(args[i.ToString()].Message!))
			.ToArray();
		return new CallState(BlockLayout.Build(new TreeNode([.. items], guide), width, fluid));
	}

	private static MString Step(MString piece, int width) =>
		piece.DisplayWidth == width ? piece : piece.Pad(MarkupText.Space, width, PadType.Right, TruncationType.Truncate);

	/// <summary>
	/// <c>node(&lt;content&gt;[, &lt;child1&gt;[, ... &lt;childN&gt;]])</c> — one item of a <c>tree()</c> and the
	/// items under it. On its own it draws as a tree of one.
	/// </summary>
	[SharpFunction(Name = "node", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["content", "child..."])]
	public ValueTask<CallState> Node(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		if (LayoutWidth(parser, MarkupText.Empty) is not (int width, bool fluid)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));

		var children = args.Keys.Select(int.Parse).Where(i => i > 0).Order()
			.SelectMany(i => TreeItemsOf(args[i.ToString()].Message!))
			.ToArray();
		var item = new TreeItem(Body(Arg(args, 0)), [.. children]);
		return ValueTask.FromResult(new CallState(BlockLayout.Build(new TreeNode([item], TreeGuide.Line), width, fluid)));
	}

	/// <summary>An argument as tree items: the items a <c>node()</c> or <c>tree()</c> made, or the content as a leaf.</summary>
	private static IEnumerable<TreeItem> TreeItemsOf(MString content) =>
		BlockLayout.AsNode(content) is TreeNode { Items.IsDefaultOrEmpty: false } tree
			? tree.Items
			: [new TreeItem(Body(content))];

	/// <summary>Whether <c>image_hosts</c> and <c>image_host_list</c> let the game show <paramref name="address"/>.</summary>
	private bool ImageAllowed(string address)
	{
		var cosmetic = Configuration.CurrentValue.Cosmetic;
		return ImageHostPolicy.Allows(address, cosmetic.ImageHosts, cosmetic.ImageHostList);
	}

	private BorderStyle DefaultBorder() =>
		BorderStyle.Preset(Configuration.CurrentValue.Cosmetic.LayoutBorder ?? string.Empty) ?? BorderStyle.Mush;

	/// <summary>An argument's content as a flex item: the item an <c>item()</c> made, or the content at an automatic width.</summary>
	private static FlexItem FlexItemOf(MString content) =>
		BlockLayout.AsNode(content) is FlexNode { Items.Length: 1 } single
			? single.Items[0]
			: new FlexItem(Body(content));

	/// <summary>A body as one node: its blocks and the text between them, in order.</summary>
	private static LayoutNode Body(MString content, Alignment alignment = Alignment.Left)
	{
		var nodes = BlockLayout.Nodes(content, alignment);
		return nodes.Count switch
		{
			0 => new TextNode(MarkupText.Empty, alignment),
			1 => nodes[0],
			_ => new StackNode([.. nodes]),
		};
	}

	/// <summary>
	/// The width a layout is drawn at: the number given, or for an empty argument or <c>auto</c>, the
	/// width the connection that ran the command reported (78 when there is none), marked fluid so
	/// each reader is sent it at their own width. Null when the argument is not a width.
	/// </summary>
	private (int Width, bool Fluid)? LayoutWidth(IMUSHCodeParser parser, MString arg)
	{
		var text = arg.ToPlainText().Trim();
		if (text.Length == 0 || text.Equals("auto", StringComparison.OrdinalIgnoreCase))
		{
			var reported = parser.CurrentState.Handle is { } handle
				&& ConnectionService.Get(handle)?.Metadata.GetValueOrDefault("WIDTH") is { } value
				&& int.TryParse(value, out var cells) && cells > 0
					? cells
					: 78;
			return (Math.Min(reported, MaxLayoutWidth), true);
		}

		return int.TryParse(text, out var width) && width is > 0 and <= MaxLayoutWidth ? (width, false) : null;
	}

	private static bool TryCount(MString arg, int fallback, out int value)
	{
		var text = arg.ToPlainText().Trim();
		value = fallback;
		return text.Length == 0 || (int.TryParse(text, out value) && value is >= 0 and <= MaxLayoutWidth);
	}

	private static MString Arg(IReadOnlyDictionary<string, CallState> args, int index) =>
		args.TryGetValue(index.ToString(), out var arg) && arg.Message is { } message ? message : MarkupText.Empty;
}
