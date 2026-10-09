using System.Collections.Immutable;
using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Markup;

/// <summary>
/// Themes as softcode names them: a preset by name (<c>nord</c>), or a palette written out as JSON
/// (<c>{"seed":"#7aa2f7","harmony":"triadic"}</c>), read by <see cref="ThemePalette.TryParse(string, out ThemePalette?, out string?)"/>.
/// The presets are SharpMUSH's own (<see cref="Own"/>) and MarkupString's (<see cref="ThemePalette.Presets"/>).
/// </summary>
public static class LayoutThemes
{
	/// <summary>What an unknown theme name answers.</summary>
	public const string Unknown = "#-1 UNKNOWN THEME";

	/// <summary>The theme a game is drawn in until its <c>layout_theme</c> names another.</summary>
	public const string Default = SharpMUSHName;

	/// <summary>What <c>layout_theme</c> is set to for layouts without colour.</summary>
	public const string None = "none";

	private const string SharpMUSHName = "sharpmush";

	/// <summary>
	/// SharpMUSH's look: the portal's teal on black. Borders and headings take the portal's dimmer teal, titles,
	/// labels and bullets its bright one, as the portal draws a layout.
	/// </summary>
	public static ThemePalette SharpMUSH { get; } = Ported(SharpMUSHName, ThemeMode.Dark,
		["#000000", "#16181b", "#e9edf0", "#00bf8f", "#00f5b7", "#00f5b7", "#7d8790", "#6cde9a", "#ffb454", "#e57373", "#5aa9ff"]);

	/// <summary>
	/// SharpMUSH's presets: <see cref="SharpMUSH"/>, then the portal's built-in themes under the portal's ids, but for
	/// its genre themes, whose ids are MarkupString's genres. Each is the portal theme's colours copied by hand:
	/// background <c>bg</c>, surface <c>surface-2</c>, foreground <c>text</c>, primary <c>accent</c>, secondary
	/// <c>accent-2</c> (<c>text</c> for a theme without one), tertiary <c>accent-3</c> (<c>warn</c> without one), muted
	/// <c>text-faint</c>, then the status colours the portal works out for the theme (success, <c>warn</c>, danger and
	/// info). A colour that did not stand out from the background enough for its role was moved toward white on a dark
	/// theme, black on a light one, until it did.
	/// </summary>
	public static IReadOnlyList<ThemePalette> Own { get; } =
	[
		SharpMUSH,
		Ported("phosphor", ThemeMode.Dark, ["#0e0f11", "#1c1f23", "#e9edf0", "#00f5b7", "#e9edf0", "#ffb454", "#7d8790", "#6cde9a", "#ffb454", "#e57373", "#5aa9ff"]),
		Ported("daylight", ThemeMode.Light, ["#f3f4f6", "#eef0f3", "#14181c", "#00775f", "#14181c", "#8f5300", "#5f6872", "#387350", "#8f5300", "#a05050", "#3a6ca3"]),
		Ported("magical-girl", ThemeMode.Light, ["#fcedf7", "#f9eafa", "#2e1538", "#b8157a", "#7e5cb9", "#8f4b00", "#6b5279", "#387350", "#8f4b00", "#9c4e4e", "#38699e"]),
		Ported("shojo", ThemeMode.Light, ["#fdf8f1", "#fbf3ea", "#2b2226", "#0f6f73", "#ab5851", "#815fbd", "#695a60", "#3a7853", "#8f4b00", "#a55353", "#3a6ca3"]),
		Ported("idol", ThemeMode.Dark, ["#0c0a1d", "#1c163f", "#f4efff", "#ff58a8", "#36e2ff", "#ffde4a", "#9e94c4", "#6cde9a", "#ffd84a", "#e57373", "#5aa9ff"]),
		Ported("slice-of-life", ThemeMode.Light, ["#fbf7ec", "#f8f3e6", "#2a2722", "#1f68a8", "#856f3e", "#4d7a6b", "#615a51", "#387350", "#8f4b00", "#a55353", "#3a6ca3"]),
		Ported("shonen", ThemeMode.Light, ["#f4f2ee", "#f0eeea", "#0d0d0d", "#c42d12", "#0d0d0d", "#8f4b00", "#545454", "#387350", "#8f4b00", "#9c4e4e", "#38699e"]),
		Ported("sports", ThemeMode.Dark, ["#0a1428", "#162644", "#eef3fb", "#ff8c2e", "#eef3fb", "#ffd166", "#8d9bb4", "#6cde9a", "#ffd166", "#e57373", "#5aa9ff"]),
		Ported("mechmachine", ThemeMode.Dark, ["#16181b", "#272b30", "#e7e9eb", "#ff6b1f", "#f0b820", "#ffc62a", "#8f969d", "#6cde9a", "#ffc62a", "#e57373", "#5aa9ff"]),
		Ported("isekai", ThemeMode.Dark, ["#0a0e2c", "#18225a", "#eaf0ff", "#ffd35e", "#eaf0ff", "#ffb35c", "#919dd0", "#6cde9a", "#ffb35c", "#e57373", "#5aa9ff"]),
		Ported("yokai", ThemeMode.Dark, ["#0c0e1b", "#1b1f34", "#efe6d4", "#f2643c", "#ffecdc", "#e8b04a", "#958d80", "#6cde9a", "#e8b04a", "#e57373", "#5aa9ff"]),
		Ported("comic-book", ThemeMode.Light, ["#f5eed6", "#fff3c4", "#111111", "#c8141c", "#7a6c24", "#0072a3", "#4d4d4d", "#3a7853", "#8a5200", "#a55353", "#3a6ca3"]),
		Ported("rubber-hose", ThemeMode.Light, ["#ebe1c6", "#ede2c2", "#1c1814", "#a3302a", "#1c1814", "#7a4d00", "#564b3f", "#326647", "#7a4d00", "#934a4a", "#346294"]),
		Ported("eighties-cartoon", ThemeMode.Dark, ["#190e3c", "#2e1d63", "#fff6ff", "#ffe23a", "#ff2fb4", "#22e1ff", "#b3a1d6", "#6cde9a", "#ffa94d", "#e57373", "#5aa9ff"]),
		Ported("cyberpunk", ThemeMode.Dark, ["#07060b", "#19131f", "#f2edf7", "#ff2bd6", "#05d9e8", "#f3e600", "#9d90ad", "#6cde9a", "#f3e600", "#e57373", "#5aa9ff"]),
		Ported("synthwave", ThemeMode.Dark, ["#1a0b2e", "#311552", "#fdeeff", "#ff6b9d", "#ffd166", "#ff8c42", "#bfa2d2", "#6cde9a", "#ffd166", "#e57373", "#5aa9ff"]),
		Ported("space-opera", ThemeMode.Dark, ["#04050b", "#1c1f28", "#eee7d4", "#f0b429", "#8cb4ff", "#ffe2be", "#9a927f", "#6cde9a", "#ff8c42", "#e57373", "#5aa9ff"]),
		Ported("starship-console", ThemeMode.Dark, ["#000000", "#16141c", "#f5e9da", "#ff9933", "#c39be0", "#8fa6ff", "#a99a8b", "#6cde9a", "#ffcc66", "#e57373", "#5aa9ff"]),
		Ported("real-robot", ThemeMode.Light, ["#e4e8ee", "#f0f3f7", "#121a28", "#1a49b5", "#c91e27", "#7c6200", "#4f5a6b", "#387350", "#8a5a00", "#a05050", "#38699e"]),
		Ported("super-robot", ThemeMode.Dark, ["#160609", "#2c1116", "#fff3e3", "#ffc82a", "#ff7a1a", "#dd413c", "#c09a8e", "#6cde9a", "#ff9a3c", "#e57373", "#5aa9ff"]),
		Ported("protan-dark", ThemeMode.Dark, ["#0e0f11", "#1c1f23", "#e9edf0", "#a8e8fa", "#e9edf0", "#f2ef0a", "#7d8790", "#2ca1c1", "#f2ef0a", "#f58b28", "#9f5efe"]),
		Ported("protan-light", ThemeMode.Light, ["#f3f4f6", "#eef0f3", "#14181c", "#1b729b", "#14181c", "#876c00", "#5f6872", "#0d155a", "#876c00", "#612f08", "#5801f5"]),
		Ported("deutan-dark", ThemeMode.Dark, ["#0e0f11", "#1c1f23", "#e9edf0", "#b6edfa", "#e9edf0", "#fde30b", "#7d8790", "#21b4e0", "#fde30b", "#f58b28", "#8869fe"]),
		Ported("deutan-light", ThemeMode.Light, ["#f3f4f6", "#eef0f3", "#14181c", "#045781", "#14181c", "#816f08", "#5f6872", "#030964", "#816f08", "#702504", "#4f00f3"]),
		Ported("tritan-dark", ThemeMode.Dark, ["#0e0f11", "#1c1f23", "#e9edf0", "#ff93c4", "#e9edf0", "#fce49e", "#7d8790", "#2dd2fd", "#fce49e", "#ff2018", "#1380f5"]),
		Ported("tritan-light", ThemeMode.Light, ["#f3f4f6", "#eef0f3", "#14181c", "#d10759", "#14181c", "#8d6a02", "#5f6872", "#027398", "#8d6a02", "#700612", "#052c63"]),
		Ported("mono-dark", ThemeMode.Dark, ["#0e0f11", "#1c1f23", "#e9edf0", "#318ec0", "#e9edf0", "#e4e2c8", "#7d8790", "#ade2cc", "#e4e2c8", "#bf6767", "#ccd3da"]),
		Ported("mono-light", ThemeMode.Light, ["#f3f4f6", "#eef0f3", "#14181c", "#193055", "#14181c", "#777130", "#5f6872", "#297295", "#777130", "#561b16", "#1f264c"]),
	];

	private static readonly ImmutableDictionary<string, ThemePalette> OwnByName =
		Own.ToImmutableDictionary(theme => theme.Name, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// A palette from the colours of <see cref="ThemeRole"/>s in this order: background, surface, foreground, primary,
	/// secondary, tertiary, muted, success, warning, error, info.
	/// </summary>
	private static ThemePalette Ported(string name, ThemeMode mode, string[] colors)
	{
		ThemeRole[] roles =
		[
			ThemeRole.Background, ThemeRole.Surface, ThemeRole.Foreground, ThemeRole.Primary, ThemeRole.Secondary,
			ThemeRole.Tertiary, ThemeRole.Muted, ThemeRole.Success, ThemeRole.Warning, ThemeRole.Error, ThemeRole.Info,
		];
		return new ThemePalette
		{
			Name = name,
			Mode = mode,
			Colors = roles.Zip(colors).ToImmutableDictionary(pair => pair.First,
				pair => ColorMath.TryParseHex(pair.Second, out var rgb) ? ThemeColor.Of(rgb) : throw new FormatException(pair.Second)),
		};
	}

	/// <summary>SharpMUSH's preset named <paramref name="name"/>, ignoring case, or null.</summary>
	public static ThemePalette? OwnPreset(string name) => OwnByName.GetValueOrDefault(name.Trim());

	/// <summary>Whether <paramref name="name"/> is a built-in theme, SharpMUSH's or MarkupString's.</summary>
	public static bool IsBuiltIn(string name) => OwnPreset(name) is not null || ThemePalette.Preset(name.Trim()) is not null;

	/// <summary>The palette <paramref name="spec"/> names or writes out, or the error saying why not.</summary>
	public static Result<ThemePalette> Read(string spec)
	{
		spec = spec.Trim();
		if (spec.Length == 0) return new Error<string>(Unknown);
		if (OwnPreset(spec) is { } own) return own;
		if (ThemePalette.TryParse(spec, out var palette, out var error)) return palette!;
		var unknown = spec[0] is not ('{' or '[') && (error ?? string.Empty).Contains("theme name", StringComparison.Ordinal)
			|| (error ?? string.Empty).StartsWith("no theme named", StringComparison.Ordinal);
		return new Error<string>(unknown ? Unknown : $"#-1 INVALID THEME: {error}");
	}

	/// <summary>The preset names, in the order they are listed: SharpMUSH's, then MarkupString's.</summary>
	public static IEnumerable<string> Names => Own.Concat(ThemePalette.Presets).Select(preset => preset.Name);
}
