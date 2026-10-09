using System.Collections.Immutable;
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
/// SharpMUSH's own layout functions: <c>box()</c>, <c>rule()</c>, <c>flex()</c>, <c>item()</c>,
/// <c>figure()</c>, <c>fields()</c>, <c>tree()</c> and <c>node()</c>. Each returns the text a terminal
/// shows — the same box art <c>align()</c> and <c>repeat()</c> would draw — with the block it was
/// drawn from riding on it, so the portal draws a bordered card whose columns wrap on a phone, and a
/// telnet client is sent the box again at its own width. A function given another's result nests that
/// block rather than its text. <c>align()</c>, <c>center()</c> and the rest are unchanged.
/// </summary>
/// <remarks>
/// Every function reads its options through one <see cref="OptionSchema{T}"/>, so they take the same
/// spellings and fail the same way; <see cref="LayoutOptionKeys"/> lists each one's keys.
/// </remarks>
public partial class Functions
{
	/// <summary>A block being built and the <c>width:</c> it was given, with the game's border for pieces set without a preset.</summary>
	private sealed record Laid<T>(T Block, MString Width, BorderStyle House, Func<string, Result<ThemePalette>> ReadTheme) where T : Block
	{
		/// <summary>The border the call's options chose, for the block and every box and rule inside it that names none.</summary>
		public BorderStyle? Border { get; init; }

		/// <summary>The colours the call's <c>theme</c> option chose, for the block and everything inside it.</summary>
		public LayoutTheme? Theme { get; init; }

		/// <summary>The ansi() codes of the <c>stripe</c> option's colour, empty for the theme's.</summary>
		public MString? Stripe { get; init; }
	}

	/// <summary>
	/// The border options on a layout that holds others: they set the border of the boxes and rules
	/// inside it. Its <c>theme</c> colours them all.
	/// </summary>
	private static OptionSchema<Laid<T>> InnerBorder<T>(OptionSchema<Laid<T>> schema) where T : Block =>
		schema.Border(laid => laid.Border ?? laid.House, (laid, border) => laid with { Border = border })
			.Theme(laid => laid.ReadTheme, (laid, theme) => laid with { Theme = theme });

	private static OptionSchema<Laid<Frame>> BoxSchema { get; } = OptionSchema<Laid<Frame>>.Empty
		.Border(laid => laid.Block.Border ?? laid.House, (laid, border) => laid with { Block = laid.Block with { Border = border }, Border = border })
		.Choice("title", LayoutOptionGroups.Alignments, (laid, alignment) => laid with { Block = laid.Block with { TitleAlignment = alignment } })
		.Int("pad", 0, 10, (laid, pad) => laid with { Block = laid.Block with { Padding = pad } })
		.Titles("titles", (laid, titles) => laid with { Block = laid.Block with { Titles = titles } })
		.Titles("bottomtitles", (laid, titles) => laid with { Block = laid.Block with { BottomTitles = titles } })
		.Theme(laid => laid.ReadTheme, (laid, theme) => laid with { Theme = theme });

	private static OptionSchema<Laid<Rule>> RuleSchema { get; } = OptionSchema<Laid<Rule>>.Empty
		.Border(laid => laid.Block.Border ?? laid.House, (laid, border) => laid with { Block = laid.Block with { Border = border } })
		.Choice("title", LayoutOptionGroups.Alignments, (laid, alignment) => laid with { Block = laid.Block with { TitleAlignment = alignment } })
		.Titles("titles", (laid, titles) => laid with { Block = laid.Block with { Titles = titles } })
		.Theme(laid => laid.ReadTheme, (laid, theme) => laid with { Theme = theme });

	private static OptionSchema<Laid<Flex>> FlexSchema { get; } = InnerBorder(OptionSchema<Laid<Flex>>.Empty)
		.Width((laid, width) => laid with { Width = width })
		.Int("gap", 0, LayoutOptionGroups.MaxGap, (laid, gap) => laid with { Block = laid.Block with { Gap = gap } })
		.Text("sep", (laid, separator) => laid with { Block = laid.Block with { Separator = separator } })
		.Choice("justify", Names<FlexJustify>(), (laid, justify) => laid with { Block = laid.Block with { Justify = justify } })
		.Choice("align", new Dictionary<string, FlexAlign>
		{
			["top"] = FlexAlign.Start,
			["center"] = FlexAlign.Center,
			["centre"] = FlexAlign.Center,
			["bottom"] = FlexAlign.End,
		}, (laid, align) => laid with { Block = laid.Block with { Align = align } })
		.Flag("vertical", (laid, vertical) => laid with { Block = laid.Block with { Vertical = vertical } });

	private static OptionSchema<Laid<Fields>> FieldsSchema { get; } = InnerBorder(OptionSchema<Laid<Fields>>.Empty)
		.Width((laid, width) => laid with { Width = width })
		.Choice("align", new Dictionary<string, Alignment> { ["left"] = Alignment.Left, ["right"] = Alignment.Right },
			(laid, alignment) => laid with { Block = laid.Block with { LabelAlignment = alignment } })
		.Text("sep", (laid, separator) => laid with { Block = laid.Block with { Separator = separator } })
		.Text("leader", (laid, leader) => laid with { Block = laid.Block with { Leader = leader.Length == 0 ? null : leader } })
		.Int("cols", 1, 10, (laid, columns) => laid with { Block = laid.Block with { Columns = columns } })
		.Int("gap", 0, LayoutOptionGroups.MaxGap, (laid, gap) => laid with { Block = laid.Block with { Gap = gap } })
		.Stripe((laid, codes) => laid with { Block = laid.Block with { Striped = codes is not null }, Stripe = codes });

	private static OptionSchema<Laid<Tree>> TreeSchema { get; } = InnerBorder(OptionSchema<Laid<Tree>>.Empty)
		.Width((laid, width) => laid with { Width = width })
		.Custom("guide", (laid, value) => TreeGuide.Preset(value.ToPlainText().Trim()) is { } preset
			? laid with { Block = laid.Block with { Guide = preset } }
			: new Error<string>("#-1 UNKNOWN GUIDE STYLE"), first: true)
		.Text("branch", (laid, piece) => WithGuide(laid, guide => guide with { Branch = piece }))
		.Text("last", (laid, piece) => WithGuide(laid, guide => guide with { Last = piece }))
		.Text("pipe", (laid, piece) => WithGuide(laid, guide => guide with { Pipe = piece }))
		.Text("blank", (laid, piece) => WithGuide(laid, guide => guide with { Blank = piece }));

	private static Laid<Tree> WithGuide(Laid<Tree> laid, Func<TreeGuide, TreeGuide> change) =>
		laid with { Block = laid.Block with { Guide = change(laid.Block.Guide ?? TreeGuide.Line) } };

	/// <summary>Each layout function's option keys, as its schema declares them: what its help must list.</summary>
	public static IReadOnlyDictionary<string, IEnumerable<string>> LayoutOptionKeys => new Dictionary<string, IEnumerable<string>>
	{
		["box"] = BoxSchema.Keys,
		["rule"] = RuleSchema.Keys,
		["flex"] = FlexSchema.Keys,
		["fields"] = FieldsSchema.Keys,
		["tree"] = TreeSchema.Keys,
		["gauge"] = GaugeSchema.Keys,
		["bullets"] = BulletsSchema.Keys,
		["grid"] = GridSchema.Keys,
		["datatable"] = DataTableSchema.Keys,
		["datacolumns"] = DataTableSchema.Keys,
		["gradient"] = GradientSchema.Keys,
	};

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
		var title = Arg(args, 1);
		var frame = new Frame(Body(Arg(args, 0))) { Title = title.Length == 0 ? null : title };
		return ValueTask.FromResult(Laidout(parser, BoxSchema, Arg(args, 3), frame, Arg(args, 2)));
	}

	/// <summary>
	/// <c>rule([&lt;title&gt;[, &lt;width&gt;[, &lt;options&gt;]]])</c> — a line across the width with the
	/// title set into it. Inside a <c>box()</c> it divides the box, in the box's border unless it names one.
	/// </summary>
	[SharpFunction(Name = "rule", MinArgs = 0, MaxArgs = 3, Flags = FunctionFlags.Regular, ParameterNames = ["title", "width", "options"])]
	public ValueTask<CallState> Rule(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var title = Arg(args, 0);
		return ValueTask.FromResult(Laidout(parser, RuleSchema, Arg(args, 2), new Rule(title.Length == 0 ? null : title), Arg(args, 1)));
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
		var items = Rest(args, 1).Select(FlexItemOf).ToImmutableArray();
		return ValueTask.FromResult(Laidout(parser, FlexSchema, Arg(args, 0), new Flex(items)));
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
		if (!LayoutOptionGroups.TryItemWidth(Arg(args, 1).ToPlainText(), out var basis, out var alignment)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.InvalidArgument));
		if (!TryCount(Arg(args, 2), 1, out var min) || !TryCount(Arg(args, 3), 0, out var grow)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));
		if (LayoutWidth(parser, MarkupText.Empty) is not (int width, bool fluid)) return ValueTask.FromResult(new CallState(ErrorMessages.Returns.ArgRange));

		var item = Body(Arg(args, 0), alignment).Sized(basis, Math.Max(1, min), grow);
		return ValueTask.FromResult(new CallState(Build(new Flex([item]), width, fluid)));
	}

	/// <summary>
	/// <c>figure(&lt;address&gt;[, &lt;description&gt;[, &lt;art&gt;[, &lt;float&gt;[, &lt;beside&gt;[, &lt;width&gt;]]]]])</c>
	/// — a picture with the text art a terminal shows instead of it, and, floated <c>left</c> or
	/// <c>right</c>, text that flows round it. The picture is shown only when the caller may show
	/// pictures (as <c>image()</c>: Send_Image or Send_OOB) and <c>image_hosts</c> allows its host;
	/// otherwise the art is.
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
			&& await CanSendImage(await parser.CurrentState.KnownExecutorObject(Mediator))
			&& ImageAllowed(address);
		var description = Arg(args, 1).ToPlainText();
		var image = new ImageMarkup(shown ? address : string.Empty, description.Length == 0 ? null : description);
		var beside = Arg(args, 4);

		var figure = new Figure(image, Arg(args, 2)) { Float = floated, Beside = beside.Length == 0 ? null : Body(beside) };
		return new CallState(Build(figure, width, fluid));
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

		var fields = new List<Field>();
		for (var i = 1; i + 1 < args.Count; i += 2)
			fields.Add(new Field(Arg(args, i), Body(Arg(args, i + 1))));
		return ValueTask.FromResult(Laidout(parser, FieldsSchema, Arg(args, 0), new Fields([.. fields])));
	}

	/// <summary>
	/// <c>tree([&lt;options&gt;], &lt;item1&gt;[, ... &lt;itemN&gt;])</c> — items with the items under them,
	/// joined by guide lines. An item is text, or a <c>node()</c> that has items of its own.
	/// </summary>
	[SharpFunction(Name = "tree", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["options", "item..."])]
	public ValueTask<CallState> Tree(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var tree = new Tree([.. Rest(args, 1).SelectMany(TreeItemsOf)]);
		return ValueTask.FromResult(TreeSchema.Apply(Arg(args, 0), new Laid<Tree>(tree, MarkupText.Empty, DefaultBorder(), LayoutThemeService.Read)) switch
		{
			Laid<Tree> laid => Finish(parser, laid.Block with { Guide = laid.Block.Guide is { } guide ? Even(guide) : null }, laid.Width, laid.Border, laid.Theme),
			Error<string> error => new CallState(error.Value),
		});
	}

	/// <summary>The guide with each piece padded or cut to the width of the branch, so the levels line up.</summary>
	private static TreeGuide Even(TreeGuide guide)
	{
		var step = Math.Max(guide.Branch.DisplayWidth, guide.Last.DisplayWidth);
		return guide with
		{
			Branch = Step(guide.Branch, step),
			Last = Step(guide.Last, step),
			Pipe = Step(guide.Pipe, step),
			Blank = Step(guide.Blank, step),
		};
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

		var item = new TreeItem(Body(Arg(args, 0)), [.. Rest(args, 1).SelectMany(TreeItemsOf)]);
		return ValueTask.FromResult(new CallState(Build(new Tree([item]), width, fluid)));
	}

	/// <summary>
	/// <paramref name="block"/> with <paramref name="options"/> applied through <paramref name="schema"/>
	/// and laid out at the width an option or <paramref name="width"/> names.
	/// </summary>
	private CallState Laidout<T>(IMUSHCodeParser parser, OptionSchema<Laid<T>> schema, MString options, T block, MString? width = null) where T : Block =>
		schema.Apply(options, new Laid<T>(block, width ?? MarkupText.Empty, DefaultBorder(), LayoutThemeService.Read),
			laid => Finish(parser, laid.Block, laid.Width, laid.Border, laid.Theme, StripeOf(laid.Stripe)),
			error => new CallState(error.Value));

	/// <summary>
	/// <paramref name="block"/> laid out at the width <paramref name="widthArg"/> names, under
	/// <paramref name="border"/>, <paramref name="theme"/> and <paramref name="stripe"/> when its options
	/// chose them: the boxes and rules inside it that name no border of their own take that one instead
	/// of <c>layout_border</c>, and its parts take the theme's colours instead of <c>layout_theme</c>'s.
	/// </summary>
	private CallState Finish(IMUSHCodeParser parser, Block block, MString widthArg, BorderStyle? border = null, LayoutTheme? theme = null, IMarkup? stripe = null) =>
		LayoutWidth(parser, widthArg) is (int width, bool fluid)
			? new CallState(Build(border is null && theme is null && stripe is null
				? block
				: block.Themed((theme ?? LayoutTheme.Default) with { Border = border ?? theme?.Border, StripeColor = stripe ?? theme?.StripeColor }), width, fluid, block is Table { Striped: true } or Fields { Striped: true }))
			: new CallState(ErrorMessages.Returns.ArgRange);

	/// <summary>The <c>stripe</c> option's colour, from its ansi() codes, or null for the theme's.</summary>
	private IMarkup? StripeOf(MString? codes) => codes is { Length: > 0 } ? AnsiCodes(codes.ToPlainText()) : null;

	/// <summary>
	/// The stripe when neither the call, the reader nor <c>layout_theme</c> names one: a dark grey, for the dark
	/// background most clients have. Only a striped layout carries it, so no other sets the colour in a browser.
	/// </summary>
	private static readonly IMarkup DefaultStripe = new AnsiMarkup(new AnsiStyle { Background = new AnsiColor.Rgb(48, 48, 48) });

	/// <summary>
	/// <paramref name="block"/> laid out under the game's look (<c>layout_border</c>, <c>layout_theme</c>),
	/// which a block that takes it in as a child sheds again so the outer one's look carries through
	/// (<see cref="Adopt"/>). The game's look sits under any a reader brings (<see cref="Themed.Fallback"/>).
	/// </summary>
	/// <param name="striped">Whether it is striped, so the game's look needs a stripe colour even when <c>layout_theme</c> has none.</param>
	private MString Build(Block block, int width, bool fluid, bool striped = false)
	{
		var house = HouseTheme();
		if (striped && house.StripeColor is null) house = house with { StripeColor = DefaultStripe };
		return BlockLayout.Build(block.ThemedUnder(house), width, fluid);
	}

	/// <summary>The <c>layout_border</c> and resolved <c>layout_theme</c> the house look was last made from, and the look.</summary>
	private (string? Border, string? Theme, ThemePalette? Palette, LayoutTheme Look)? _house;

	/// <summary>The game's look: <c>layout_border</c>'s border and <c>layout_theme</c>'s colours.</summary>
	private LayoutTheme HouseTheme() => House().Look;

	/// <summary><c>layout_theme</c>'s palette, or null when it is unset or names no theme.</summary>
	private ThemePalette? HousePalette() => House().Palette;

	private (string? Border, string? Theme, ThemePalette? Palette, LayoutTheme Look) House()
	{
		var cosmetic = Configuration.CurrentValue.Cosmetic;
		// Keyed on what layout_theme resolves to, so a change to an added theme it names is seen.
		var unset = string.IsNullOrWhiteSpace(cosmetic.LayoutTheme) || cosmetic.LayoutTheme.Trim().Equals(LayoutThemes.None, StringComparison.OrdinalIgnoreCase);
		var theme = unset ? null : LayoutThemeService.Resolve(cosmetic.LayoutTheme) switch
		{
			string resolved => resolved,
			_ => string.Empty,
		};
		if (_house is { } house && house.Border == cosmetic.LayoutBorder && house.Theme == theme) return house;
		var palette = string.IsNullOrEmpty(theme) ? null : LayoutThemes.Read(theme) switch
		{
			ThemePalette read => read,
			_ => null,
		};
		// A theme with a look of its own brings its border; layout_border is the border of one without.
		var colours = palette?.ToLayoutTheme() ?? LayoutTheme.Default;
		var look = colours with { Border = colours.Border ?? DefaultBorder() };
		var made = (cosmetic.LayoutBorder, theme, palette, look);
		_house = made;
		return made;
	}

	/// <summary>A child block without the game's look a function laid it out under.</summary>
	private Block Adopt(Block block) => block is Themed { Fallback: true } themed && themed.Theme == HouseTheme() ? themed.Content : block;

	/// <summary>The arguments from <paramref name="first"/> on, in order.</summary>
	private static IEnumerable<MString> Rest(IReadOnlyDictionary<string, CallState> args, int first) =>
		args.Keys.Select(int.Parse).Where(i => i >= first).Order().Select(i => args[i.ToString()].Message);

	/// <summary>An argument as tree items: the items a <c>node()</c> or <c>tree()</c> made, or the content as a leaf.</summary>
	private IEnumerable<TreeItem> TreeItemsOf(MString content) =>
		Adopt(BlockLayout.AsBlock(content)) is Tree { Items.IsDefaultOrEmpty: false } tree
			? tree.Items
			: [new TreeItem(Body(content))];

	/// <summary>Whether <c>image_hosts</c> and <c>image_host_list</c> let the game show <paramref name="address"/>.</summary>
	private bool ImageAllowed(string address)
	{
		var cosmetic = Configuration.CurrentValue.Cosmetic;
		return ImageHostPolicy.Allows(address, cosmetic.ImageHosts, cosmetic.ImageHostList);
	}

	private BorderStyle DefaultBorder() =>
		BorderStyle.Preset(Configuration.CurrentValue.Cosmetic.LayoutBorder ?? string.Empty) ?? BorderStyle.Double;

	/// <summary>An argument as a flex item: the sized item an <c>item()</c> made, or the content at an automatic width.</summary>
	private Block FlexItemOf(MString content) =>
		Adopt(BlockLayout.AsBlock(content)) is Flex { Items: [Sized single] } ? single : Body(content);

	/// <summary>A body as one block: its blocks and the text between them, in order.</summary>
	private Block Body(MString content, Alignment? alignment = null)
	{
		var blocks = BlockLayout.Blocks(content, alignment);
		return blocks.Count switch
		{
			0 => new TextBlock(MarkupText.Empty) { Alignment = alignment },
			1 => Adopt(blocks[0]),
			_ => new Stack([.. blocks.Select(Adopt)]),
		};
	}

	/// <summary>An enum's values by their lower-case names.</summary>
	private static Dictionary<string, TEnum> Names<TEnum>() where TEnum : struct, Enum =>
		Enum.GetValues<TEnum>().ToDictionary(value => value.ToString().ToLowerInvariant());

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
		args.TryGetValue(index.ToString(), out var arg) ? arg.Message : MarkupText.Empty;
}
