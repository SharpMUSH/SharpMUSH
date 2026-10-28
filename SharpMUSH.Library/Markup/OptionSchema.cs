using System.Collections.Immutable;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Layout;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// The options one layout function takes, each a key and what its value does to the settings
/// <typeparamref name="T"/> the function builds from. The options are a JSON object,
/// <c>{"border":"double","pad":1}</c>, written in softcode inside a second pair of braces or built with
/// <c>json()</c>. A string value keeps its markup, so a border piece can be coloured.
/// </summary>
/// <remarks>
/// Options apply in the order given, except those declared <c>first</c> (a preset another option
/// changes a piece of), which apply before the rest. A number option takes a JSON number, an on-or-off
/// option <c>true</c> or <c>false</c>, and every other option a string (or a number, read as its text).
/// Every function reports bad options the same way: <see cref="NotAnObject"/> for text that is not one
/// JSON object of plain values, <c>#-1 UNKNOWN LAYOUT OPTION &lt;KEY&gt;</c> for a key it does not take,
/// <c>#-1 DUPLICATE LAYOUT OPTION &lt;KEY&gt;</c> for a key given twice, <see cref="ErrorMessages.Returns.ArgRange"/>
/// for a number out of range, and <see cref="ErrorMessages.Returns.InvalidArgument"/> for any other bad value.
/// </remarks>
/// <typeparam name="T">The settings the options change.</typeparam>
public sealed class OptionSchema<T> where T : class
{
	private readonly ImmutableArray<Option> _options;

	private OptionSchema(ImmutableArray<Option> options) => _options = options;

	/// <summary>A schema with no options.</summary>
	public static OptionSchema<T> Empty { get; } = new([]);

	/// <summary>What the options answer when they are not one JSON object of strings, numbers and booleans.</summary>
	public const string NotAnObject = "#-1 LAYOUT OPTIONS MUST BE A JSON OBJECT";

	/// <summary>The keys this schema takes, in the order they were declared.</summary>
	public IEnumerable<string> Keys => _options.Select(option => option.Key);

	/// <summary>An option whose value is what <paramref name="apply"/> makes of it, or the error it returns.</summary>
	/// <param name="key">The key, lower case.</param>
	/// <param name="apply">The settings with the value applied, or an error.</param>
	/// <param name="first">Whether it applies before the options that are not first, wherever it is written.</param>
	public OptionSchema<T> Custom(string key, Func<T, MString, Result<T>> apply, bool first = false) =>
		Typed(key, ValueKind.String | ValueKind.Number, apply, first);

	private OptionSchema<T> Typed(string key, ValueKind accepts, Func<T, MString, Result<T>> apply, bool first = false) =>
		new(_options.Add(new Option(key, first, accepts, apply)));

	/// <summary>An option that takes a string, or a JSON object or array written as it is, which <paramref name="apply"/> reads as JSON text.</summary>
	public OptionSchema<T> Json(string key, Func<T, MString, Result<T>> apply) =>
		Typed(key, ValueKind.String | ValueKind.Json, apply);

	/// <summary>An option that takes any text, empty included.</summary>
	public OptionSchema<T> Text(string key, Func<T, MString, T> set) =>
		Custom(key, (settings, value) => set(settings, value));

	/// <summary>An option that takes any text but none.</summary>
	public OptionSchema<T> NonEmptyText(string key, Func<T, MString, T> set) =>
		Custom(key, (settings, value) => value.Length == 0 ? Invalid : set(settings, value));

	/// <summary>An option that takes a whole number from <paramref name="min"/> to <paramref name="max"/>.</summary>
	public OptionSchema<T> Int(string key, int min, int max, Func<T, int, T> set) =>
		Typed(key, ValueKind.Number, (settings, value) => int.TryParse(value.ToPlainText().Trim(), out var number)
			? number >= min && number <= max ? set(settings, number) : OutOfRange
			: Invalid);

	/// <summary>An option that takes one of <paramref name="choices"/>' names, in any case.</summary>
	public OptionSchema<T> Choice<TValue>(string key, IReadOnlyDictionary<string, TValue> choices, Func<T, TValue, T> set) =>
		Custom(key, (settings, value) => choices.TryGetValue(value.ToPlainText().Trim().ToLowerInvariant(), out var choice)
			? set(settings, choice)
			: Invalid);

	/// <summary>An option that is on as <c>true</c> and off as <c>false</c>.</summary>
	public OptionSchema<T> Flag(string key, Func<T, bool, T> set) =>
		Typed(key, ValueKind.Boolean, (settings, value) => set(settings, value.Text == "true"));

	/// <summary>An option that takes <c>true</c> or <c>false</c>, or text: what <paramref name="set"/> is given for each, the text empty for <c>true</c> and null for <c>false</c>.</summary>
	public OptionSchema<T> FlagOrText(string key, Func<T, MString?, T> set) =>
		Typed(key, ValueKind.Boolean | ValueKind.String, (settings, value) => value.ToPlainText() switch
		{
			"true" => set(settings, MarkupText.Empty),
			"false" => set(settings, null),
			_ => set(settings, value),
		});

	/// <summary>This schema's options followed by <paramref name="group"/>'s.</summary>
	public OptionSchema<T> Including(Func<OptionSchema<T>, OptionSchema<T>> group) => group(this);

	/// <summary><paramref name="settings"/> with the options in <paramref name="text"/> applied, or the first error.</summary>
	public Result<T> Apply(MString text, T settings) => OptionObject.Read(text) switch
	{
		IReadOnlyList<OptionObject.Member> members => Apply(members, settings),
		Error<string> error => error,
	};

	private Result<T> Apply(IReadOnlyList<OptionObject.Member> members, T settings)
	{
		var pairs = new List<(Option Option, MString Value)>();
		foreach (var member in members)
		{
			if (_options.FirstOrDefault(option => option.Key == member.Key) is not { } option)
				return new Error<string>($"#-1 UNKNOWN LAYOUT OPTION {member.Key.ToUpperInvariant()}");
			if (pairs.Any(pair => pair.Option == option))
				return new Error<string>($"#-1 DUPLICATE LAYOUT OPTION {member.Key.ToUpperInvariant()}");
			if ((option.Accepts & member.Kind) == 0) return OptionObject.Named(option.Key, new Error<string>(ErrorMessages.Returns.InvalidArgument));
			pairs.Add((option, member.Value));
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
				Error<string> error => OptionObject.Named(pairs[index].Option.Key, error),
				_ => throw new InvalidOperationException("A layout option returned neither settings nor an error."),
			};

	private static Result<T> Invalid => new Error<string>(ErrorMessages.Returns.InvalidArgument);

	private static Result<T> OutOfRange => new Error<string>(ErrorMessages.Returns.ArgRange);

	private sealed record Option(string Key, bool First, ValueKind Accepts, Func<T, MString, Result<T>> Apply);
}

/// <summary>The kinds of JSON value a layout option may be given.</summary>
[Flags]
public enum ValueKind
{
	/// <summary>A JSON string.</summary>
	String = 1,

	/// <summary>A JSON number.</summary>
	Number = 2,

	/// <summary><c>true</c> or <c>false</c>.</summary>
	Boolean = 4,

	/// <summary>A JSON object or array, kept as the text it was written as, markup included.</summary>
	Json = 8,
}

/// <summary>
/// A layout function's options read as one JSON object. Read over the marked-up text rather than its
/// plain text, so a string value keeps the colour it was written in; an escape becomes a plain character.
/// A string may hold a newline or a tab as it is, so <c>%r</c> and <c>%t</c> work inside the braces.
/// </summary>
public static class OptionObject
{
	/// <summary>One member: its key, lower-cased, the kind of value, and the value as text.</summary>
	public sealed record Member(string Key, ValueKind Kind, MString Value);

	/// <summary>
	/// The members of the object <paramref name="text"/> holds, in order; none for empty text. Text that does not
	/// begin as an object is <see cref="OptionSchema{T}.NotAnObject"/>; an object that breaks off says what was
	/// expected and at which character.
	/// </summary>
	public static Result<IReadOnlyList<Member>> Read(MString text)
	{
		var reader = new Reader(text);
		reader.SkipSpace();
		if (reader.AtEnd) return new List<Member>();
		if (reader.Peek != '{') return new Error<string>(OptionSchema<object>.NotAnObject);
		if (reader.ReadObject() is not { } members) return reader.Failure;
		reader.SkipSpace();
		return reader.AtEnd ? members : reader.Fail("TEXT AFTER THE OBJECT");
	}

	/// <summary>
	/// The items of the array <paramref name="text"/> holds, in order, each an object's members; a string item is
	/// read as an object whose only member is <c>text</c>. Null when it is not such an array.
	/// </summary>
	public static IReadOnlyList<IReadOnlyList<Member>>? ReadArray(MString text)
	{
		var reader = new Reader(text);
		var items = new List<IReadOnlyList<Member>>();
		reader.SkipSpace();
		if (!reader.Take('[')) return null;
		reader.SkipSpace();
		if (!reader.Take(']'))
		{
			while (true)
			{
				reader.SkipSpace();
				if (reader.AtEnd) return null;
				if (reader.Peek == '"')
				{
					if (reader.ReadString() is not { } value) return null;
					items.Add([new Member("text", ValueKind.String, value)]);
				}
				else if (reader.ReadObject() is { } members)
				{
					items.Add(members);
				}
				else
				{
					return null;
				}
				reader.SkipSpace();
				if (reader.Take(']')) break;
				if (!reader.Take(',')) return null;
			}
		}
		reader.SkipSpace();
		return reader.AtEnd ? items : null;
	}

	/// <summary>
	/// <paramref name="error"/> with the option it came from named, when it is one of the two errors any option
	/// can give (<see cref="ErrorMessages.Returns.InvalidArgument"/>, <see cref="ErrorMessages.Returns.ArgRange"/>):
	/// <c>#-1 LAYOUT OPTION GROW: INVALID ARGUMENT</c>. An error of an option's own already says what is wrong.
	/// </summary>
	public static Error<string> Named(string key, Error<string> error) =>
		error.Value is ErrorMessages.Returns.InvalidArgument or ErrorMessages.Returns.ArgRange
			? new Error<string>($"#-1 LAYOUT OPTION {key.ToUpperInvariant()}: {error.Value["#-1 ".Length..]}")
			: error;

	/// <summary>A walk over the text of the options, which remembers the first place it could not read.</summary>
	private sealed class Reader(MString text)
	{
		private readonly string _source = text.Text;
		private int _position;
		private (string Expected, int Position)? _failed;

		public bool AtEnd => _position == _source.Length;

		public char Peek => _source[_position];

		/// <summary>Why the text could not be read, and the character (from 1) where reading stopped.</summary>
		public Error<string> Failure => _failed is var (expected, position)
			? new Error<string>($"#-1 LAYOUT OPTIONS: {expected} AT CHARACTER {position + 1}")
			: new Error<string>(OptionSchema<object>.NotAnObject);

		/// <summary>Notes, the first time, what was expected here; <see cref="Failure"/> reports it.</summary>
		public Error<string> Fail(string expected)
		{
			_failed ??= (expected, _position);
			return Failure;
		}

		public void SkipSpace()
		{
			while (!AtEnd && Peek is ' ' or '\t' or '\n' or '\r') _position++;
		}

		/// <summary>Steps over <paramref name="c"/> when it is next.</summary>
		public bool Take(char c)
		{
			if (AtEnd || Peek != c) return false;
			_position++;
			return true;
		}

		/// <summary>The object here, its members in order; null when it is not one.</summary>
		public List<Member>? ReadObject()
		{
			var members = new List<Member>();
			if (!Take('{'))
			{
				Fail("EXPECTED {");
				return null;
			}

			SkipSpace();
			if (Take('}')) return members;

			while (true)
			{
				SkipSpace();
				if (AtEnd || Peek != '"')
				{
					Fail("EXPECTED A KEY IN DOUBLE QUOTES");
					return null;
				}
				if (ReadString() is not { } key) return null;
				SkipSpace();
				if (!Take(':'))
				{
					Fail("EXPECTED :");
					return null;
				}
				SkipSpace();
				if (ReadValue() is not var (kind, value)) return null;
				members.Add(new Member(key.ToPlainText().ToLowerInvariant(), kind, value));
				SkipSpace();
				if (Take('}')) return members;
				if (!Take(','))
				{
					Fail("EXPECTED , OR }");
					return null;
				}
			}
		}

		private (ValueKind Kind, MString Value)? ReadValue()
		{
			if (AtEnd)
			{
				Fail("EXPECTED A VALUE");
				return null;
			}
			if (Peek == '"') return ReadString() is { } value ? (ValueKind.String, value) : null;
			if (Peek is '{' or '[')
			{
				// Kept with its markup, so a title in an array of titles keeps its colour.
				var opened = _position;
				return SkipNested() ? (ValueKind.Json, text.Substring(opened, _position - opened)) : null;
			}

			foreach (var literal in (ReadOnlySpan<string>)["true", "false"])
			{
				if (string.CompareOrdinal(_source, _position, literal, 0, literal.Length) != 0) continue;
				_position += literal.Length;
				return (ValueKind.Boolean, MarkupText.Plain(literal));
			}

			var start = _position;
			if (!AtEnd && Peek == '-') _position++;
			var digits = _position;
			while (!AtEnd && (char.IsAsciiDigit(Peek) || Peek is '.' or 'e' or 'E' or '+' or '-')) _position++;
			var number = _source[start.._position];
			if (_position > digits && double.TryParse(number, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
				return (ValueKind.Number, MarkupText.Plain(number));
			_position = start;
			Fail("EXPECTED A VALUE");
			return null;
		}

		/// <summary>
		/// Steps over the object or array here, up to the bracket that closes it, strings skipped whole; whether it
		/// is valid JSON is for the option reading it to say.
		/// </summary>
		private bool SkipNested()
		{
			var depth = 0;
			var inString = false;
			for (; !AtEnd; _position++)
			{
				var c = Peek;
				if (inString)
				{
					if (c == '\\') _position++;
					else if (c == '"') inString = false;
					continue;
				}
				if (c == '"') inString = true;
				else if (c is '{' or '[') depth++;
				else if (c is '}' or ']' && --depth == 0)
				{
					_position++;
					return true;
				}
			}
			Fail("EXPECTED ] OR }");
			return false;
		}

		/// <summary>The string here, its unescaped runs cut from the text with their markup.</summary>
		public MString? ReadString()
		{
			if (!Take('"')) return null;

			var parts = new List<MString>();
			var run = _position;
			while (!AtEnd)
			{
				var c = Peek;
				if (c == '"')
				{
					parts.Add(text.Substring(run, _position - run));
					_position++;
					return MarkupText.Concat([.. parts]);
				}

				if (c != '\\')
				{
					_position++;
					continue;
				}

				parts.Add(text.Substring(run, _position - run));
				if (++_position == _source.Length) break;
				var escaped = _source[_position] switch
				{
					'"' => "\"",
					'\\' => "\\",
					'/' => "/",
					'b' => "\b",
					'f' => "\f",
					'n' => "\n",
					'r' => "\r",
					't' => "\t",
					'u' when _position + 5 <= _source.Length
						&& int.TryParse(_source.AsSpan(_position + 1, 4), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var code)
						=> ((char)code).ToString(),
					_ => null,
				};
				if (escaped is null)
				{
					_position--;
					Fail("UNKNOWN ESCAPE");
					return null;
				}
				_position += Peek == 'u' ? 5 : 1;
				parts.Add(MarkupText.Plain(escaped));
				run = _position;
			}

			Fail("EXPECTED \" TO CLOSE THE STRING");
			return null;
		}
	}
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

	/// <summary>The parts of a layout a gradient falls on.</summary>
	public static readonly IReadOnlyDictionary<string, GradientTarget> Targets = new Dictionary<string, GradientTarget>
	{
		["all"] = GradientTarget.All,
		["borders"] = GradientTarget.Borders,
		["text"] = GradientTarget.Text,
	};

	/// <summary>
	/// <c>theme:</c> a theme by name, or one written out as a JSON object, read by <paramref name="read"/> (the game's
	/// themes, <c>ILayoutThemeService.Read</c>), for the layout and everything inside it.
	/// </summary>
	public static OptionSchema<T> Theme<T>(this OptionSchema<T> schema, Func<T, Func<string, Result<ThemePalette>>> read, Func<T, LayoutTheme, T> set) where T : class =>
		schema.Json("theme", (settings, value) => read(settings)(value.ToPlainText()) switch
		{
			ThemePalette palette => set(settings, palette.ToLayoutTheme()),
			Error<string> error => error,
		});

	/// <summary>
	/// A list of titles for a line or an edge (<c>titles</c> on <c>rule()</c> and <c>box()</c>, <c>bottomtitles</c> on
	/// <c>box()</c>): a JSON array whose items are a title's text, set in the middle, or an object with <c>text</c>,
	/// <c>side</c> (<c>left</c>, <c>center</c> or <c>right</c>) and <c>priority</c> (1 to 1000; the highest is left out first
	/// when they do not fit, and a title without one takes its side's: left 1, right 2, middle 3).
	/// </summary>
	public static OptionSchema<T> Titles<T>(this OptionSchema<T> schema, string key, Func<T, ImmutableArray<EdgeTitle>, T> set) where T : class =>
		schema.Json(key, (settings, value) => OptionObject.ReadArray(value) is { } items
			? ReadTitles(items) switch
			{
				ImmutableArray<EdgeTitle> titles => set(settings, titles),
				Error<string> error => error,
			}
			: new Error<string>(ErrorMessages.Returns.InvalidArgument));

	private static Result<ImmutableArray<EdgeTitle>> ReadTitles(IReadOnlyList<IReadOnlyList<OptionObject.Member>> items)
	{
		var titles = ImmutableArray.CreateBuilder<EdgeTitle>(items.Count);
		foreach (var item in items)
		{
			var title = new EdgeTitle(MarkupText.Empty);
			foreach (var member in item)
			{
				switch (member.Key)
				{
					case "text" when member.Kind is ValueKind.String or ValueKind.Number:
						title = title with { Text = member.Value };
						break;
					case "side" when member.Kind == ValueKind.String && Alignments.TryGetValue(member.Value.ToPlainText().Trim().ToLowerInvariant(), out var side):
						title = title with { Side = side };
						break;
					case "priority" when member.Kind == ValueKind.Number:
						if (!int.TryParse(member.Value.ToPlainText(), out var priority) || priority is < 1 or > 1000)
							return new Error<string>(ErrorMessages.Returns.ArgRange);
						title = title with { Priority = priority };
						break;
					default:
						return new Error<string>(ErrorMessages.Returns.InvalidArgument);
				}
			}
			titles.Add(title);
		}
		return titles.ToImmutable();
	}

	/// <summary>
	/// <c>stripe:</c> <c>true</c> lays every second row on the theme's stripe colour, and ansi() codes
	/// (<c>/#303030</c>) on that colour instead; the codes are kept as written for the function to read.
	/// </summary>
	public static OptionSchema<T> Stripe<T>(this OptionSchema<T> schema, Func<T, MString?, T> set) where T : class =>
		schema.FlagOrText("stripe", set);

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
				? set(settings, (get(settings) ?? new ColorGradient([])) with { Stops = stops, Roles = Roles(value) })
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
		var stops = StopTexts(list).Select(color).ToArray();
		return stops.Length == 0 || stops.Any(stop => stop is not { Foreground: not null }) ? null : [.. stops.OfType<IColorMarkup>()];
	}

	/// <summary>
	/// The theme colour each stop names, by position: a tone() name's, so a reader's theme can recolour it, or
	/// null for ansi() codes. Empty when none names one.
	/// </summary>
	public static ImmutableArray<ThemeRole?> Roles(MString list)
	{
		ThemeRole?[] roles = [.. StopTexts(list).Select(text => ToneMarkup.TryParse(text, out var tone) ? tone.Role : (ThemeRole?)null)];
		return roles.Any(role => role is not null) ? [.. roles] : [];
	}

	private static IEnumerable<string> StopTexts(MString list) =>
		MushText.SplitList(MarkupText.Plain("|"), list)
			.Select(codes => codes.ToPlainText().Trim())
			.Where(text => text.Length > 0);

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
