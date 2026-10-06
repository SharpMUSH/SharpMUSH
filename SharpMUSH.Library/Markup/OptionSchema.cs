using System.Collections.Immutable;
using MarkupString;
using MarkupString.Layout;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// The options one layout function takes, each a key and what its value does to the settings
/// <typeparamref name="T"/> the function builds from. An option list is <c>key:value</c> pairs split by
/// spaces, a value in double quotes when it holds a space (<c>open:"&lt;&lt; "</c>); a value keeps its
/// markup, so a border piece can be coloured.
/// </summary>
/// <remarks>
/// Options apply in the order given, except those declared <c>first</c> (a preset another option
/// changes a piece of), which apply before the rest. Every function reports a bad list the same way:
/// <c>#-1 UNKNOWN LAYOUT OPTION &lt;KEY&gt;</c> for a key it does not take, <see cref="ErrorMessages.Returns.ArgRange"/>
/// for a number out of range, and <see cref="ErrorMessages.Returns.InvalidArgument"/> for any other bad value.
/// </remarks>
/// <typeparam name="T">The settings the options change.</typeparam>
public sealed class OptionSchema<T> where T : class
{
	private readonly ImmutableArray<Option> _options;

	private OptionSchema(ImmutableArray<Option> options) => _options = options;

	/// <summary>A schema with no options.</summary>
	public static OptionSchema<T> Empty { get; } = new([]);

	/// <summary>The keys this schema takes, in the order they were declared.</summary>
	public IEnumerable<string> Keys => _options.Select(option => option.Key);

	/// <summary>An option whose value is what <paramref name="apply"/> makes of it, or the error it returns.</summary>
	/// <param name="key">The key, lower case.</param>
	/// <param name="apply">The settings with the value applied, or an error.</param>
	/// <param name="first">Whether it applies before the options that are not first, wherever it is written.</param>
	public OptionSchema<T> Custom(string key, Func<T, MString, Result<T>> apply, bool first = false) =>
		new(_options.Add(new Option(key, first, apply)));

	/// <summary>An option that takes any text, empty included.</summary>
	public OptionSchema<T> Text(string key, Func<T, MString, T> set) =>
		Custom(key, (settings, value) => set(settings, value));

	/// <summary>An option that takes any text but none.</summary>
	public OptionSchema<T> NonEmptyText(string key, Func<T, MString, T> set) =>
		Custom(key, (settings, value) => value.Length == 0 ? Invalid : set(settings, value));

	/// <summary>An option that takes a whole number from <paramref name="min"/> to <paramref name="max"/>.</summary>
	public OptionSchema<T> Int(string key, int min, int max, Func<T, int, T> set) =>
		Custom(key, (settings, value) => int.TryParse(value.ToPlainText().Trim(), out var number)
			? number >= min && number <= max ? set(settings, number) : OutOfRange
			: Invalid);

	/// <summary>An option that takes one of <paramref name="choices"/>' names, in any case.</summary>
	public OptionSchema<T> Choice<TValue>(string key, IReadOnlyDictionary<string, TValue> choices, Func<T, TValue, T> set) =>
		Custom(key, (settings, value) => choices.TryGetValue(value.ToPlainText().Trim().ToLowerInvariant(), out var choice)
			? set(settings, choice)
			: Invalid);

	/// <summary>An option that is on when written bare, or as <c>yes</c>, <c>on</c>, <c>true</c> or <c>1</c>, and off as <c>no</c>, <c>off</c>, <c>false</c> or <c>0</c>.</summary>
	public OptionSchema<T> Flag(string key, Func<T, bool, T> set) =>
		Custom(key, (settings, value) => value.ToPlainText().Trim().ToLowerInvariant() switch
		{
			"" or "yes" or "on" or "true" or "1" => set(settings, true),
			"no" or "off" or "false" or "0" => set(settings, false),
			_ => Invalid,
		});

	/// <summary>This schema's options followed by <paramref name="group"/>'s.</summary>
	public OptionSchema<T> Including(Func<OptionSchema<T>, OptionSchema<T>> group) => group(this);

	/// <summary><paramref name="settings"/> with the options in <paramref name="text"/> applied, or the first error.</summary>
	public Result<T> Apply(MString text, T settings)
	{
		var pairs = new List<(Option Option, MString Value)>();
		foreach (var (key, value) in Pairs(text))
		{
			if (_options.FirstOrDefault(option => option.Key == key) is not { } option)
				return new Error<string>($"#-1 UNKNOWN LAYOUT OPTION {key.ToUpperInvariant()}");
			pairs.Add((option, value));
		}

		return Run([.. pairs.Where(pair => pair.Option.First), .. pairs.Where(pair => !pair.Option.First)], 0, settings);
	}

	/// <summary>
	/// <paramref name="applied"/> given <paramref name="settings"/> with the options in <paramref name="text"/>
	/// applied, or <paramref name="failed"/> given the first error: for generic code, which C# does not let
	/// match a union whose case is a type parameter.
	/// </summary>
	public TResult Apply<TResult>(MString text, T settings, Func<T, TResult> applied, Func<Error<string>, TResult> failed) =>
		Apply(text, settings).Value switch
		{
			T done => applied(done),
			Error<string> error => failed(error),
			_ => throw new InvalidOperationException("A layout option returned neither settings nor an error."),
		};

	/// <summary><paramref name="settings"/> with the pairs from <paramref name="index"/> on applied in turn, or the first error.</summary>
	private static Result<T> Run(IReadOnlyList<(Option Option, MString Value)> pairs, int index, T settings) =>
		index == pairs.Count
			? settings
			: pairs[index].Option.Apply(settings, pairs[index].Value).Value switch
			{
				// A pattern on a union whose case is a type parameter is refused (CS8780), so the case is read
				// from the boxed value; T is a class, so it cannot be the error.
				T applied => Run(pairs, index + 1, applied),
				Error<string> error => error,
				_ => throw new InvalidOperationException("A layout option returned neither settings nor an error."),
			};

	/// <summary>The pairs in <paramref name="text"/>, in order, keys lower-cased, a bare key with an empty value.</summary>
	private static IEnumerable<(string Key, MString Value)> Pairs(MString text)
	{
		var source = text.Text;
		var position = 0;
		while (position < source.Length)
		{
			while (position < source.Length && source[position] == ' ') position++;
			if (position >= source.Length) yield break;

			var start = position;
			var quoted = false;
			while (position < source.Length && (quoted || source[position] != ' '))
			{
				if (source[position] == '"') quoted = !quoted;
				position++;
			}

			var token = text.Substring(start, position - start);
			var colon = token.Text.IndexOf(':');
			var key = (colon < 0 ? token.Text : token.Text[..colon]).ToLowerInvariant();
			var value = colon < 0 ? MarkupText.Empty : token.Substring(colon + 1);
			if (value.Length >= 2 && value.Text[0] == '"' && value.Text[^1] == '"') value = value.Substring(1, value.Length - 2);
			yield return (key, value);
		}
	}

	private static Result<T> Invalid => new Error<string>(ErrorMessages.Returns.InvalidArgument);

	private static Result<T> OutOfRange => new Error<string>(ErrorMessages.Returns.ArgRange);

	private sealed record Option(string Key, bool First, Func<T, MString, Result<T>> Apply);
}

/// <summary>Option groups more than one layout function takes, and the readers they share.</summary>
public static class LayoutOptionGroups
{
	/// <summary>Cell counts a layout option may be: a gap, a padding.</summary>
	public const int MaxGap = 20;

	/// <summary><c>left</c>, <c>center</c> or <c>right</c>, and <c>align()</c>'s <c>&lt;</c>, <c>-</c> and <c>&gt;</c>.</summary>
	public static readonly IReadOnlyDictionary<string, Alignment> Alignments = new Dictionary<string, Alignment>
	{
		["left"] = Alignment.Left,
		["<"] = Alignment.Left,
		["center"] = Alignment.Center,
		["centre"] = Alignment.Center,
		["-"] = Alignment.Center,
		["right"] = Alignment.Right,
		[">"] = Alignment.Right,
	};

	/// <summary>The spaces a gradient is blended in.</summary>
	public static readonly IReadOnlyDictionary<string, GradientSpace> Spaces = new Dictionary<string, GradientSpace>
	{
		["oklch"] = GradientSpace.Oklch,
		["oklab"] = GradientSpace.Oklab,
		["hsl"] = GradientSpace.Hsl,
	};

	/// <summary>The ways a gradient runs over text.</summary>
	public static readonly IReadOnlyDictionary<string, GradientFlow> Flows = new Dictionary<string, GradientFlow>
	{
		["characters"] = GradientFlow.Characters,
		["words"] = GradientFlow.Words,
		["across"] = GradientFlow.Across,
		["down"] = GradientFlow.Down,
		["diagonal"] = GradientFlow.Diagonal,
	};

	/// <summary><c>width:</c>, kept as written: the function reads it once it knows the connection.</summary>
	public static OptionSchema<T> Width<T>(this OptionSchema<T> schema, Func<T, MString, T> set) where T : class =>
		schema.Text("width", set);

	/// <summary>
	/// <c>border:</c> picks a preset, before the pieces whatever their order; <c>corner</c>, <c>tl</c>,
	/// <c>tr</c>, <c>bl</c>, <c>br</c>, <c>top</c>, <c>bottom</c>, <c>side</c>, <c>left</c>, <c>right</c>,
	/// <c>teel</c>, <c>teer</c>, <c>open</c> and <c>close</c> each replace their part of it.
	/// </summary>
	public static OptionSchema<T> Border<T>(this OptionSchema<T> schema, Func<T, BorderStyle> get, Func<T, BorderStyle, T> set) where T : class
	{
		OptionSchema<T> Piece(OptionSchema<T> s, string key, Func<BorderStyle, MString, BorderStyle> change) =>
			s.Text(key, (settings, value) => set(settings, change(get(settings), value)));

		schema = schema.Custom("border", (settings, value) => BorderStyle.Preset(value.ToPlainText().Trim()) is { } preset
			? set(settings, preset)
			: new Error<string>("#-1 UNKNOWN BORDER STYLE"), first: true);
		schema = Piece(schema, "corner", (style, value) => style with { TopLeft = value, TopRight = value, BottomLeft = value, BottomRight = value });
		schema = Piece(schema, "tl", (style, value) => style with { TopLeft = value });
		schema = Piece(schema, "tr", (style, value) => style with { TopRight = value });
		schema = Piece(schema, "bl", (style, value) => style with { BottomLeft = value });
		schema = Piece(schema, "br", (style, value) => style with { BottomRight = value });
		schema = Piece(schema, "top", (style, value) => style with { Top = value });
		schema = Piece(schema, "bottom", (style, value) => style with { Bottom = value });
		schema = Piece(schema, "side", (style, value) => style with { Left = value, Right = value });
		schema = Piece(schema, "left", (style, value) => style with { Left = value });
		schema = Piece(schema, "right", (style, value) => style with { Right = value });
		schema = Piece(schema, "teel", (style, value) => style with { TeeLeft = value });
		schema = Piece(schema, "teer", (style, value) => style with { TeeRight = value });
		schema = Piece(schema, "open", (style, value) => style with { TitleOpen = value });
		return Piece(schema, "close", (style, value) => style with { TitleClose = value });
	}

	/// <summary>
	/// <c>gradient:</c> the colours, ansi() codes split by <c>|</c>, and the <see cref="GradientShape"/>
	/// options that change how they run.
	/// </summary>
	public static OptionSchema<T> Gradient<T>(this OptionSchema<T> schema, Func<string, IColorMarkup?> color,
		Func<T, ColorGradient?> get, Func<T, ColorGradient, T> set) where T : class =>
		schema
			.Custom("gradient", (settings, value) => Stops(value, color) is { } stops
				? set(settings, (get(settings) ?? new ColorGradient([])) with { Stops = stops })
				: new Error<string>("#-1 UNKNOWN COLOR"))
			.GradientShape(get, set);

	/// <summary><c>space:</c> what a gradient's colours blend in; <c>mirror</c> to run there and back; <c>repeat:</c> how many times to run.</summary>
	public static OptionSchema<T> GradientShape<T>(this OptionSchema<T> schema, Func<T, ColorGradient?> get, Func<T, ColorGradient, T> set) where T : class
	{
		ColorGradient Of(T settings) => get(settings) ?? new ColorGradient([]);
		return schema
			.Choice("space", Spaces, (settings, space) => set(settings, Of(settings) with { Space = space }))
			.Flag("mirror", (settings, mirror) => set(settings, Of(settings) with { Mirror = mirror }))
			.Int("repeat", 1, 1000, (settings, repeat) => set(settings, Of(settings) with { Repeat = repeat }));
	}

	/// <summary>Colour stops, ansi() codes split by <c>|</c>; null when one sets no foreground colour or there are none.</summary>
	public static ImmutableArray<IColorMarkup>? Stops(MString list, Func<string, IColorMarkup?> color)
	{
		var stops = new List<IColorMarkup>();
		foreach (var codes in MushText.SplitList(MarkupText.Plain("|"), list))
		{
			var text = codes.ToPlainText().Trim();
			if (text.Length == 0) continue;
			if (color(text) is not { Foreground: not null } stop) return null;
			stops.Add(stop);
		}
		return stops.Count == 0 ? null : [.. stops];
	}

	/// <summary>
	/// A flex item's width: <c>auto</c>, cells (<c>35</c>) or a percentage (<c>40%</c>), optionally after
	/// <c>align()</c>'s justification character (<c>&lt;</c> left, <c>-</c> centre, <c>&gt;</c> right).
	/// </summary>
	public static bool TryItemWidth(string text, out BlockSize size, out Alignment alignment)
	{
		text = text.Trim();
		alignment = Alignment.Left;
		if (text.Length > 0 && Alignments.TryGetValue(text[..1], out var placed) && text[0] is '<' or '-' or '>')
		{
			alignment = placed;
			text = text[1..];
		}
		return BlockSize.TryParse(text, out size);
	}
}
