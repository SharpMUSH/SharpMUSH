using System.Collections.Immutable;
using System.Globalization;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Layout;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// A layout as the call that builds it: <c>box()</c>, <c>rule()</c>, <c>flex()</c> and <c>item()</c>,
/// <c>figure()</c>, <c>fields()</c>, <c>tree()</c> and <c>node()</c>, <c>gauge()</c>, <c>bullets()</c>,
/// <c>grid()</c>, <c>datatable()</c> and <c>gradient()</c>, read back from the tree the layout carries.
/// </summary>
/// <remarks>
/// <para>Only what differs from a function's defaults is written, so <c>box(x)</c> comes back as
/// <c>[box(x)]</c>. The game's <c>layout_border</c> and <c>layout_theme</c> are not written: they are laid
/// under the tree, not in it, and the call takes whatever the game has then.</para>
/// <para>Options are a JSON object in braces, <c>{"border":"double"}</c>; one whose values carry colour or
/// characters the parser reads is written escaped instead, so it evaluates to the same object.</para>
/// <para>Some calls cannot be told apart from another that draws the same: <c>datacolumns()</c> comes back
/// as <c>datatable()</c>, a lone <c>node()</c> as a <c>tree()</c> of one, a <c>badge()</c> as the
/// <c>ansi()</c> it is. A picture the game would not show has no address to write, and a theme that is
/// neither a named one nor colours alone is left out.</para>
/// </remarks>
public static partial class SoftcodeDecomposer
{
	/// <summary>
	/// The layout over run <paramref name="first"/>, the last run it covers, and the markup outside it;
	/// null when the run is not the start of a layout whose text is still as it was laid out.
	/// </summary>
	private static (LayoutMarkup Layout, int Last, IMarkup[] Outside)? LayoutAt(MarkupText text, int first)
	{
		var runs = text.Runs;
		var markups = runs[first].Markups;
		var index = -1;
		for (var i = markups.Count - 1; i >= 0; i--)
		{
			if (markups[i] is not LayoutMarkup) continue;
			index = i;
			break;
		}
		if (index < 0) return null;

		var layout = (LayoutMarkup)markups[index];
		var last = first;
		while (last + 1 < runs.Length && runs[last + 1].Start == runs[last].End && runs[last + 1].Markups.Contains(layout)) last++;
		var start = runs[first].Start;
		return layout.Covers(text.Text.AsSpan(start, runs[last].End - start))
			? (layout, last, [.. markups.Skip(index + 1)])
			: null;
	}

	/// <summary>
	/// The call that builds <paramref name="layout"/>, or its text escaped when no call does: one laid out
	/// under a look other than <paramref name="house"/> (a striped one is given a stripe colour of its own).
	/// </summary>
	private static string LayoutCall(LayoutMarkup layout, MarkupText drawn, LayoutTheme? house) =>
		(layout.Root is Themed { Fallback: true } under && house is not null && under.Theme with { StripeColor = house.StripeColor } != house
			? null
			: Softcode(layout.Root, layout.Fluid ? null : layout.Width, null)) ?? Escape(drawn.ToPlainText());

	/// <summary>
	/// The softcode for <paramref name="block"/>: a call, or its text. <paramref name="width"/> is the width
	/// the call names, null for the reader's own; <paramref name="theme"/> the look the call's options set.
	/// Null for a block no function builds.
	/// </summary>
	private static string? Softcode(Block block, int? width, LayoutTheme? theme) => block switch
	{
		// The game's look, laid under every layout a function builds.
		Themed { Fallback: true } house => Softcode(house.Content, width, theme),
		Themed themed => Softcode(themed.Content, width, themed.Theme),
		TextBlock text => Decompose(text.Content),
		Stack stack => Join(stack.Children),
		Frame frame => BoxCall(frame, width, theme),
		Rule rule => RuleCall(rule, width, theme),
		Flex { Items: [Sized item], Gap: 2, Separator: null, Justify: FlexJustify.Start, Align: FlexAlign.Start, Vertical: false } when width is null && theme is null => ItemCall(item),
		Flex flex => FlexCall(flex, width, theme),
		Sized item => ItemCall(item),
		Figure figure => FigureCall(figure, width),
		Fields fields => FieldsCall(fields, width, theme),
		Tree tree => TreeCall(tree, width, theme),
		Gauge gauge => GaugeCall(gauge, width, theme),
		Bullets bullets => BulletsCall(bullets, width, theme),
		Grid grid => GridCall(grid, width, theme),
		Table table => TableCall(table, width, theme),
		Shaded shaded => ShadeCall(shaded, width, theme),
		// Colour or alignment laid over a whole block: no call keeps it when the block is laid out again.
		_ => null,
	};

	/// <summary>A block inside another: its call, or the text it draws when no call builds it.</summary>
	private static string Child(Block block, int? width = null, LayoutTheme? theme = null) =>
		Softcode(block, width, theme) ?? Decompose(MarkupText.Join(MarkupText.NewLine, LayoutContext.Default.Lines(block, width ?? 78)));

	/// <summary>Blocks one under another, as a body writes them: each on a line of its own.</summary>
	private static string Join(IEnumerable<Block> blocks) => string.Join("%r", blocks.Select(block => Child(block)));

	private static string BoxCall(Frame frame, int? width, LayoutTheme? theme)
	{
		var options = new Options();
		var border = frame.Border ?? ThemeBorder(theme);
		if (border is not null) options.Border(border);
		if (frame.TitleAlignment != Alignment.Center) options.Text("title", Name(frame.TitleAlignment));
		if (frame.Padding != 1) options.Number("pad", frame.Padding);
		options.Theme(theme);
		return Call("box", Child(frame.Body), Text(frame.Title), Width(width), options.Write());
	}

	private static string RuleCall(Rule rule, int? width, LayoutTheme? theme)
	{
		var options = new Options();
		var border = rule.Border ?? ThemeBorder(theme);
		if (border is not null) options.Border(border);
		if (rule.TitleAlignment != Alignment.Center) options.Text("title", Name(rule.TitleAlignment));
		options.Theme(theme);
		return Call("rule", Text(rule.Title), Width(width), options.Write());
	}

	private static string FlexCall(Flex flex, int? width, LayoutTheme? theme)
	{
		var options = Container(width, theme);
		if (flex.Gap != 2) options.Number("gap", flex.Gap);
		if (flex.Separator is { } separator) options.Text("sep", separator);
		if (flex.Justify != FlexJustify.Start) options.Text("justify", flex.Justify.ToString().ToLowerInvariant());
		if (flex.Align != FlexAlign.Start) options.Text("align", flex.Align switch { FlexAlign.Center => "center", _ => "bottom" });
		if (flex.Vertical) options.Flag("vertical");
		options.Theme(theme);
		return Call("flex", [options.Write(), .. flex.Items.Select(item => item is Sized sized ? ItemCall(sized) : Child(item))], keepEmpty: true);
	}

	/// <summary><c>item()</c>: the content, its width after the alignment its text was given, its minimum and its share.</summary>
	private static string ItemCall(Sized item)
	{
		var placed = Alignments(item.Content).FirstOrDefault() switch
		{
			Alignment.Center => "-",
			Alignment.Right => ">",
			_ => string.Empty,
		};
		var size = item.Basis.Kind == BlockSizeKind.Auto ? placed.Length > 0 ? "auto" : string.Empty : item.Basis.ToString();
		return Call("item", Child(item.Content), Escape(placed + size),
			item.Min != 1 ? Number(item.Min) : string.Empty,
			item.Grow != 0 ? Number(item.Grow) : string.Empty);
	}

	/// <summary>The alignment set on the text of <paramref name="block"/>, as <c>item()</c> sets it.</summary>
	private static IEnumerable<Alignment> Alignments(Block block) => block switch
	{
		TextBlock { Alignment: { } alignment } => [alignment],
		Stack stack => stack.Children.SelectMany(Alignments),
		_ => [],
	};

	private static string FigureCall(Figure figure, int? width) =>
		Call("figure",
			Escape(figure.Image.Source),
			Escape(figure.Image.Description ?? string.Empty),
			Decompose(figure.Art),
			figure.Float switch { FigureFloat.Left => "left", FigureFloat.Right => "right", _ => string.Empty },
			figure.Beside is { } beside ? Child(beside) : string.Empty,
			Width(width));

	private static string FieldsCall(Fields fields, int? width, LayoutTheme? theme)
	{
		var options = Container(width, theme);
		if (fields.LabelAlignment != Alignment.Left) options.Text("align", Name(fields.LabelAlignment));
		if (fields.Separator is { } separator) options.Text("sep", separator);
		if (fields.Leader is { } leader) options.Text("leader", leader);
		if (fields.Columns != 1) options.Number("cols", fields.Columns);
		if (fields.Gap != 3) options.Number("gap", fields.Gap);
		options.Theme(theme, fields.Striped);
		return Call("fields", [options.Write(), .. fields.Items.SelectMany(field => (string[])[Decompose(field.Label), Child(field.Value)])], keepEmpty: true);
	}

	private static string TreeCall(Tree tree, int? width, LayoutTheme? theme)
	{
		var options = Container(width, theme);
		if (tree.Guide is { } guide)
		{
			var preset = TreeGuide.Preset(guide.Name) ?? TreeGuide.Line;
			options.Text("guide", preset.Name);
			foreach (var (key, piece) in (ReadOnlySpan<(string, Func<TreeGuide, MarkupText>)>)
				[("branch", g => g.Branch), ("last", g => g.Last), ("pipe", g => g.Pipe), ("blank", g => g.Blank)])
			{
				if (!piece(guide).Equals(piece(preset))) options.Text(key, piece(guide));
			}
		}
		options.Theme(theme);
		return Call("tree", [options.Write(), .. tree.Items.Select(NodeCall)], keepEmpty: true);
	}

	/// <summary>A tree's item: its content alone, or a <c>node()</c> with the items under it.</summary>
	private static string NodeCall(TreeItem item) =>
		item.Children.IsDefaultOrEmpty
			? Child(item.Content)
			: Call("node", [Child(item.Content), .. item.Children.Select(NodeCall)]);

	private static string GaugeCall(Gauge gauge, int? width, LayoutTheme? theme)
	{
		var options = new Options();
		if (width is { } cells) options.Number("width", cells);
		if (gauge.Filled is { } filled) options.Text("filled", filled);
		if (gauge.Empty is { } empty) options.Text("empty", empty);
		if (gauge.Open is { } open) options.Text("open", open);
		if (gauge.Close is { } close) options.Text("close", close);
		if (gauge.Show != GaugeShow.Percent) options.Text("show", gauge.Show.ToString().ToLowerInvariant());
		if (gauge.BarWidth != 0) options.Number("bar", gauge.BarWidth);
		if (gauge.Gradient is { } gradient && Stops(gradient) is { } stops)
		{
			options.Text("gradient", stops);
			GradientShape(options, gradient);
		}
		if (gauge.Shade != GaugeShade.Cells) options.Text("shade", gauge.Shade.ToString().ToLowerInvariant());
		options.Theme(theme);
		return Call("gauge", Number(gauge.Value), Number(gauge.Maximum), Text(gauge.Label), options.Write());
	}

	private static string BulletsCall(Bullets bullets, int? width, LayoutTheme? theme)
	{
		var options = Container(width, theme);
		if (bullets.Style == BulletStyle.Custom && bullets.Marker is { } marker) options.Text("marker", marker);
		else if (bullets.Style != BulletStyle.Bullet) options.Text("style", bullets.Style.ToString().ToLowerInvariant());
		if (bullets.Start != 1) options.Number("start", bullets.Start);
		options.Theme(theme);
		var delimiter = Delimiter(bullets.Items.Select(Drawn), true);
		return Call("bullets", string.Join(ListDelimiter(delimiter), bullets.Items.Select(item => Child(item))), delimiter == " " ? string.Empty : Escape(delimiter), options.Write());
	}

	private static string GridCall(Grid grid, int? width, LayoutTheme? theme)
	{
		var options = Container(width, theme);
		if (grid.Gap != 2) options.Number("gap", grid.Gap);
		if (grid.Across) options.Flag("across");
		options.Theme(theme);
		var delimiter = Delimiter(grid.Items.Select(item => item.ToPlainText()), true);
		return Call("grid", string.Join(ListDelimiter(delimiter), grid.Items.Select(Decompose)), delimiter == " " ? string.Empty : Escape(delimiter), options.Write());
	}

	private static string TableCall(Table table, int? width, LayoutTheme? theme)
	{
		var delimiter = Delimiter(table.Columns.Select(column => column.Header.ToPlainText())
			.Concat(table.Rows.SelectMany(row => row.Select(Drawn))), false);
		var options = Container(width, theme);
		if (delimiter != "|") options.Text("delim", delimiter);
		if (table.Gap != 2) options.Number("gap", table.Gap);
		if (table.Separator is { } separator) options.Text("sep", separator);
		if (table.HeaderRule is { } rule) options.Text("rule", rule);
		foreach (var (key, value, unset) in (ReadOnlySpan<(string, Func<TableColumn, int>, int)>)
			[("priority", c => c.Priority, 1), ("min", c => c.Min, 1), ("max", c => c.Max, 0), ("grow", c => c.Grow, 0)])
		{
			var list = table.Columns.Select(column => value(column) == unset ? string.Empty : Number(value(column))).ToList();
			while (list.Count > 0 && list[^1].Length == 0) list.RemoveAt(list.Count - 1);
			if (list.Count > 0) options.Text(key, string.Join(delimiter, list));
		}
		var nowrap = table.Columns.Select((column, index) => (column, index)).Where(c => !c.column.Wrap).Select(c => Number(c.index + 1)).ToList();
		if (nowrap.Count > 0) options.Text("nowrap", string.Join(delimiter, nowrap));
		options.Theme(theme, table.Striped);

		var separatorCode = Escape(delimiter);
		return Call("datatable",
			[
				options.Write(),
				string.Join(separatorCode, table.Columns.Select(Heading)),
				.. table.Rows.Select(row => string.Join(separatorCode, row.Select(cell => Child(cell)))),
			], keepEmpty: true);
	}

	/// <summary>A heading after the character that places its column, when it is not on the left or its text starts with one.</summary>
	private static string Heading(TableColumn column)
	{
		var text = column.Header.Text;
		var placed = column.Alignment switch
		{
			Alignment.Center => "-",
			Alignment.Right => ">",
			_ => text.Length > 1 && text[0] is '<' or '-' or '>' ? "<" : string.Empty,
		};
		return Escape(placed) + Decompose(column.Header);
	}

	private static string ShadeCall(Shaded shaded, int? width, LayoutTheme? theme)
	{
		if (Stops(shaded.Gradient) is not { } stops) return Child(shaded.Content, width, theme);
		var options = new Options();
		if (shaded.Flow != GradientFlow.Characters) options.Text("flow", shaded.Flow.ToString().ToLowerInvariant());
		GradientShape(options, shaded.Gradient);
		return Call("gradient", Child(shaded.Content, width, theme), Escape(stops), options.Write());
	}

	/// <summary>A gradient's colours as ansi() codes split by <c>|</c>; null when one is not a terminal colour.</summary>
	private static string? Stops(ColorGradient gradient)
	{
		var codes = gradient.Stops.Select(stop => stop is AnsiMarkup ansi ? AnsiCodeWriter.Write(ansi.Style) : string.Empty).ToArray();
		return codes.Length == 0 || codes.Any(code => code.Length == 0) ? null : string.Join("|", codes);
	}

	private static void GradientShape(Options options, ColorGradient gradient)
	{
		if (gradient.Space != GradientSpace.Oklch) options.Text("space", gradient.Space.ToString().ToLowerInvariant());
		if (gradient.Mirror) options.Flag("mirror");
		if (gradient.Repeat != 1) options.Number("repeat", gradient.Repeat);
	}

	/// <summary>The options of a layout that holds others, starting with its <c>width</c> and the border it sets inside.</summary>
	private static Options Container(int? width, LayoutTheme? theme)
	{
		var options = new Options();
		if (width is { } cells) options.Number("width", cells);
		if (ThemeBorder(theme) is { } border) options.Border(border);
		return options;
	}

	/// <summary>The border a call's options set on top of its theme's, or null when they set none.</summary>
	private static BorderStyle? ThemeBorder(LayoutTheme? theme) =>
		theme?.Border is { } border && border != ThemeOf(theme).Look.Border ? border : null;

	/// <summary>
	/// The theme <paramref name="theme"/> was made from: a named one, or one written out from its colours,
	/// with the look it gives; none, with the default look, when it was made from neither.
	/// </summary>
	private static (string? Spec, LayoutTheme Look) ThemeOf(LayoutTheme theme)
	{
		// The call's border and stripe sit over the theme's; they are options of their own.
		bool Matches(LayoutTheme look) => theme with { Border = look.Border, StripeColor = look.StripeColor } == look;

		if (Matches(LayoutTheme.Default)) return (null, LayoutTheme.Default);
		foreach (var preset in ThemePalette.Presets)
		{
			var look = preset.ToLayoutTheme();
			if (Matches(look)) return (preset.Name, look);
		}

		if (Colours(theme) is { } palette)
		{
			var look = palette.ToLayoutTheme();
			if (Matches(look)) return (palette.ToJson(), look);
		}
		return (null, LayoutTheme.Default);
	}

	/// <summary>A palette with the colours <paramref name="theme"/>'s parts are painted in, when each part's colour reads as one.</summary>
	private static ThemePalette? Colours(LayoutTheme theme)
	{
		var colours = ImmutableDictionary.CreateBuilder<ThemeRole, ThemeColor>();
		foreach (var (role, part) in (ReadOnlySpan<(ThemeRole, IMarkup?)>)
			[(ThemeRole.Primary, theme.BorderColor), (ThemeRole.Secondary, theme.LabelColor), (ThemeRole.Tertiary, theme.BulletColor), (ThemeRole.Muted, theme.GuideColor), (ThemeRole.Surface, theme.StripeColor)])
		{
			if (part is null) continue;
			if (part is not AnsiMarkup { Style: var style }) return null;
			// The surface is painted as a background, the rest as text.
			var painted = role == ThemeRole.Surface ? ColourOf(style.Background, null) : ColourOf(style.Foreground, style.StandardForeground);
			if (painted is not { } colour) return null;
			colours[role] = colour;
		}
		return colours.Count == 0 ? null : new ThemePalette { Colors = colours.ToImmutable() };
	}

	private static ThemeColor? ColourOf(AnsiColor? colour, AnsiColor? standard)
	{
		int? Slot(AnsiColor? c) => c is AnsiColor.Standard s ? s.Index + (s.Bright ? 8 : 0) : null;
		return colour switch
		{
			AnsiColor.Rgb rgb => new ThemeColor(new RgbColor(rgb.R, rgb.G, rgb.B), Slot(standard)),
			AnsiColor.Standard => new ThemeColor(null, Slot(colour)),
			_ => null,
		};
	}

	/// <summary>
	/// A delimiter none of <paramref name="items"/> holds: a space when <paramref name="spaces"/> and none
	/// holds one, or else the first of a few characters that are rarely text.
	/// </summary>
	private static string Delimiter(IEnumerable<string> items, bool spaces)
	{
		var all = items.ToList();
		if (spaces && all.All(item => item.Length > 0 && !item.Any(char.IsWhiteSpace))) return " ";
		foreach (var candidate in (ReadOnlySpan<string>)["|", "~", "`", "^", "@", "#"])
		{
			if (!all.Any(item => item.Contains(candidate, StringComparison.Ordinal))) return candidate;
		}
		return "\u001f";
	}

	/// <summary>A delimiter between list items: a space as it is, since items never begin or end with one.</summary>
	private static string ListDelimiter(string delimiter) => delimiter == " " ? " " : Escape(delimiter);

	/// <summary>The text a block draws, ASCII and not, to tell which delimiters it holds.</summary>
	private static string Drawn(Block block) => block is TextBlock text
		? text.Content.ToPlainText()
		: string.Join("\n", LayoutContext.Default.Lines(block, 78).Concat((LayoutContext.Default with { AsciiOnly = true }).Lines(block, 78)).Select(line => line.ToPlainText()));

	/// <summary><c>[name(arg,...)]</c>, its trailing empty arguments left off unless <paramref name="keepEmpty"/> keeps the first.</summary>
	private static string Call(string name, IReadOnlyList<string> args, bool keepEmpty = false)
	{
		var count = args.Count;
		while (count > (keepEmpty ? 1 : 0) && args[count - 1].Length == 0) count--;
		return $"[{name}({string.Join(",", args.Take(count))})]";
	}

	private static string Call(string name, params string[] args) => Call(name, (IReadOnlyList<string>)args);

	private static string Text(MarkupText? text) => text is null ? string.Empty : Decompose(text);

	private static string Width(int? width) => width is { } cells ? Number(cells) : string.Empty;

	private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

	private static string Name(Alignment alignment) => alignment.ToString().ToLowerInvariant();

	/// <summary>A layout function's options, written as the JSON object it reads.</summary>
	private sealed class Options
	{
		private readonly List<(string Key, MarkupText Value)> _members = [];

		public void Text(string key, MarkupText value) => _members.Add((key, JsonString(value)));

		public void Text(string key, string value) => Text(key, MarkupText.Plain(value));

		public void Number(string key, int value) => _members.Add((key, MarkupText.Plain(value.ToString(CultureInfo.InvariantCulture))));

		public void Flag(string key) => _members.Add((key, MarkupText.Plain("true")));

		/// <summary><c>border</c>, a preset, and each piece that differs from it.</summary>
		public void Border(BorderStyle border)
		{
			var preset = BorderStyle.Preset(border.Name) ?? BorderStyle.None;
			Text("border", preset.Name);
			foreach (var (key, piece) in (ReadOnlySpan<(string, Func<BorderStyle, MarkupText>)>)
				[
					("tl", b => b.TopLeft), ("top", b => b.Top), ("tr", b => b.TopRight), ("left", b => b.Left), ("right", b => b.Right),
					("bl", b => b.BottomLeft), ("bottom", b => b.Bottom), ("br", b => b.BottomRight), ("teel", b => b.TeeLeft),
					("teer", b => b.TeeRight), ("open", b => b.TitleOpen), ("close", b => b.TitleClose),
				])
			{
				if (!piece(border).Equals(piece(preset))) Text(key, piece(border));
			}
		}

		/// <summary><c>theme</c> and <c>stripe</c>, as far as they differ from the theme's own.</summary>
		public void Theme(LayoutTheme? theme, bool striped = false)
		{
			var (spec, look) = theme is null ? (null, LayoutTheme.Default) : ThemeOf(theme);
			// A theme written out is a JSON object, kept as one.
			if (spec is not null) _members.Add(("theme", spec.StartsWith('{') ? MarkupText.Plain(spec) : JsonString(MarkupText.Plain(spec))));
			if (!striped) return;
			if (theme?.StripeColor is AnsiMarkup stripe && !Equals(stripe, look.StripeColor) && AnsiCodeWriter.Write(stripe.Style) is { Length: > 0 } codes)
				Text("stripe", codes);
			else
				Flag("stripe");
		}

		/// <summary>The object as a function argument: in braces when it is plain, escaped otherwise.</summary>
		public string Write()
		{
			if (_members.Count == 0) return string.Empty;
			var json = MarkupText.Concat(
			[
				MarkupText.Plain("{"),
				MarkupText.Join(MarkupText.Plain(","), _members.Select(member => MarkupText.Concat([JsonString(MarkupText.Plain(member.Key)), MarkupText.Plain(":"), member.Value]))),
				MarkupText.Plain("}"),
			]);
			// Braces keep the object from being evaluated, which is safe only for one the parser would leave as it is.
			var plain = json.ToPlainText();
			return json.Runs.IsDefaultOrEmpty && Balanced(plain) && !plain.Any(c => c is '[' or ']' or '%' or '\\')
				? "{" + plain + "}"
				: Decompose(json);
		}

		/// <summary>Whether each brace closes one opened before it, so the braces round the object hold all of it.</summary>
		private static bool Balanced(string json)
		{
			var depth = 0;
			foreach (var c in json)
			{
				if (c == '{') depth++;
				else if (c == '}' && --depth < 0) return false;
			}
			return depth == 0;
		}

		/// <summary><paramref name="value"/> as a JSON string, its markup kept round the characters that are not escaped.</summary>
		private static MarkupText JsonString(MarkupText value)
		{
			var parts = new List<MarkupText> { MarkupText.Plain("\"") };
			var text = value.Text;
			var run = 0;
			for (var i = 0; i < text.Length; i++)
			{
				var escaped = text[i] switch
				{
					'"' => "\\\"",
					'\\' => "\\\\",
					'\n' => "\\n",
					'\r' => "\\r",
					'\t' => "\\t",
					< ' ' => $"\\u{(int)text[i]:x4}",
					_ => null,
				};
				if (escaped is null) continue;
				parts.Add(value.Substring(run, i - run));
				parts.Add(MarkupText.Plain(escaped));
				run = i + 1;
			}
			parts.Add(value.Substring(run, text.Length - run));
			parts.Add(MarkupText.Plain("\""));
			return MarkupText.Concat([.. parts]);
		}
	}
}
