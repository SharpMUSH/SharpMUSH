using System.Globalization;
using System.Text;
using SharpMUSH.Library.API;

namespace SharpMUSH.Library.Models.Portal;

/// <summary>An sRGB colour, as the portal's tokens write it (<c>#rrggbb</c>).</summary>
public readonly record struct ThemeColor(byte R, byte G, byte B)
{
	public static readonly ThemeColor White = new(255, 255, 255);
	public static readonly ThemeColor Black = new(0, 0, 0);

	public string Hex => $"#{R:x2}{G:x2}{B:x2}";

	/// <summary>The <c>r,g,b</c> triple <c>--glow</c> carries, for <c>rgba(var(--glow), a)</c>.</summary>
	public string Triple => $"{R},{G},{B}";

	/// <summary>WCAG 2 relative luminance.</summary>
	public double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

	/// <summary>Reads <c>#rrggbb</c> or <c>#rgb</c>, with or without the <c>#</c>; nothing else.</summary>
	public static bool TryParse(string? text, out ThemeColor color)
	{
		color = default;
		var hex = text?.Trim().TrimStart('#');
		if (hex is { Length: 3 })
		{
			hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
		}

		if (hex is not { Length: 6 } || !int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var n))
		{
			return false;
		}

		color = new ThemeColor((byte)(n >> 16), (byte)(n >> 8), (byte)n);
		return true;
	}

	public static ThemeColor Parse(string text)
		=> TryParse(text, out var color) ? color : throw new FormatException($"'{text}' is not a #rrggbb colour.");

	/// <summary>The WCAG 2 contrast ratio between two colours, from 1 to 21.</summary>
	public static double Contrast(ThemeColor a, ThemeColor b)
	{
		var (hi, lo) = a.Luminance >= b.Luminance ? (a.Luminance, b.Luminance) : (b.Luminance, a.Luminance);
		return (hi + 0.05) / (lo + 0.05);
	}

	/// <summary>This colour moved <paramref name="amount"/> (0 to 1) of the way to <paramref name="other"/>.</summary>
	public ThemeColor Mix(ThemeColor other, double amount)
	{
		byte Channel(byte from, byte to) => (byte)Math.Round(from + (to - from) * Math.Clamp(amount, 0, 1));
		return new ThemeColor(Channel(R, other.R), Channel(G, other.G), Channel(B, other.B));
	}

	private static double Linear(byte channel)
	{
		var c = channel / 255.0;
		return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
	}

	public override string ToString() => Hex;
}

/// <summary>The design tokens a theme sets, and how readable each must be.</summary>
public static class ThemeTokens
{
	public const string Background = "bg";
	public const string Surface = "surface";
	public const string Surface2 = "surface-2";
	public const string Surface3 = "surface-3";
	public const string Rail = "rail-bg";
	public const string Text = "text";
	public const string TextDim = "text-dim";
	public const string TextFaint = "text-faint";
	public const string Border = "border";
	public const string BorderSoft = "border-soft";
	public const string Accent = "accent";
	public const string Warn = "warn";
	public const string LinkMissing = "link-missing";

	/// <summary>
	/// Every token a theme carries, in the order the editor lists them. Each is the name of a CSS custom property
	/// in <c>tokens.css</c> without its <c>--</c>.
	/// </summary>
	public static readonly IReadOnlyList<string> Editable =
	[
		Background, Surface, Surface2, Surface3, Rail,
		Text, TextDim, TextFaint,
		Border, BorderSoft,
		Accent, Warn, LinkMissing,
	];

	/// <summary>WCAG AA for body text (1.4.3).</summary>
	public const double TextContrast = 4.5;

	/// <summary>
	/// The pairs a theme must keep readable: each foreground token against the surface it is drawn on, at
	/// <see cref="TextContrast"/>. The theme editor shows each one's ratio and warns below the floor.
	/// </summary>
	public static readonly IReadOnlyList<(string Foreground, string Background)> ContrastPairs =
	[
		(Text, Background),
		(Text, Surface),
		(Text, Surface3),
		(TextDim, Background),
		(TextDim, Surface),
		(TextDim, Surface3),
		(TextFaint, Background),
		(TextFaint, Surface),
		(TextFaint, Surface3),
		(Accent, Background),
		(Accent, Surface),
		(Accent, Surface2),
		(Accent, Surface3),
		(Warn, Surface),
		(LinkMissing, Surface),
	];
}

/// <summary>The themes SharpMUSH ships. They are always offered, and cannot be edited or deleted.</summary>
public static class BuiltInThemes
{
	public const string PhosphorId = "phosphor";
	public const string DaylightId = "daylight";

	/// <summary>The portal's original dark look; <c>tokens.css</c> carries the same values.</summary>
	public static readonly PortalTheme Phosphor = new(PhosphorId, "Phosphor", Dark: true, Published: true,
		new Dictionary<string, string>
		{
			[ThemeTokens.Background] = "#0e0f11",
			[ThemeTokens.Surface] = "#16181b",
			[ThemeTokens.Surface2] = "#1c1f23",
			[ThemeTokens.Surface3] = "#101113",
			[ThemeTokens.Rail] = "#0a0b0c",
			[ThemeTokens.Text] = "#e9edf0",
			[ThemeTokens.TextDim] = "#9aa3ab",
			[ThemeTokens.TextFaint] = "#7d8790",
			[ThemeTokens.Border] = "#262a2f",
			[ThemeTokens.BorderSoft] = "#1d2024",
			[ThemeTokens.Accent] = "#00f5b7",
			[ThemeTokens.Warn] = "#ffb454",
			[ThemeTokens.LinkMissing] = "#ff8a8a",
		}, BuiltIn: true);

	/// <summary>A light theme, so a game has one to start from and the portal is exercised in both modes.</summary>
	public static readonly PortalTheme Daylight = new(DaylightId, "Daylight", Dark: false, Published: true,
		new Dictionary<string, string>
		{
			[ThemeTokens.Background] = "#f3f4f6",
			[ThemeTokens.Surface] = "#ffffff",
			[ThemeTokens.Surface2] = "#eef0f3",
			[ThemeTokens.Surface3] = "#e9ecef",
			[ThemeTokens.Rail] = "#e1e5e9",
			[ThemeTokens.Text] = "#14181c",
			[ThemeTokens.TextDim] = "#4a535c",
			[ThemeTokens.TextFaint] = "#5f6872",
			[ThemeTokens.Border] = "#d2d8de",
			[ThemeTokens.BorderSoft] = "#e3e7eb",
			[ThemeTokens.Accent] = "#00775f",
			[ThemeTokens.Warn] = "#8f5300",
			[ThemeTokens.LinkMissing] = "#c0362c",
		}, BuiltIn: true);

	// One theme per MSSP genre (MsspCatalog's GENRE choices, less "None"; Romance covers Adult too), with the ids the
	// telnet layout themes use, so a game's portal theme and layout_theme can share one name. Colours are listed in
	// ThemeTokens.Editable order: bg, surface, surface-2, surface-3, rail-bg, text, text-dim, text-faint, border,
	// border-soft, accent, warn, link-missing.

	/// <summary>Fantasy: scorched parchment, inscribed capitals in rubric red, fleurons and flourished corners.</summary>
	public static readonly PortalTheme Fantasy = Genre("fantasy", "Fantasy", dark: false,
		["#ecdfc2", "#f7eed6", "#eee0c0", "#e4d3ae", "#d9c59a", "#2b1d0e", "#5a4630", "#5e4a35", "#c9b083", "#dfcda5", "#8c1c13", "#8a4b00", "#a32a1d"],
		display: "cinzel", body: "serif", corners: "soft", texture: "parchment", ornament: "fleuron", frame: "filigree", titles: "normal", effect: "gilt", imagery: "sepia");

	/// <summary>Historical: a foxed old page, an old-style face pressed into it, double rules and engraved frames.</summary>
	public static readonly PortalTheme Historical = Genre("historical", "Historical", dark: false,
		["#e9e2d1", "#f5f0e3", "#ebe4d3", "#e2d9c4", "#d6cbb2", "#2a2620", "#4f4839", "#524b3c", "#c6b99b", "#ddd3bd", "#6b3e26", "#7f4f00", "#a33a2a"],
		display: "fell", body: "serif", corners: "sharp", texture: "foxing", ornament: "double", frame: "engraved", titles: "normal", effect: "emboss", imagery: "sepia");

	/// <summary>Horror: near-black pooled with blood, blackletter between daggers, glowing like embers, cards that drip.</summary>
	public static readonly PortalTheme Horror = Genre("horror", "Horror", dark: true,
		["#0c0707", "#150d0d", "#1d1212", "#100909", "#080404", "#ecdede", "#b5a3a3", "#a08e8e", "#3a2222", "#261616", "#f05a4e", "#d9a441", "#ff8a7a"],
		display: "grenze", body: "ui", corners: "sharp", texture: "blood", ornament: "cross", frame: "drip", titles: "normal", effect: "ember", imagery: "tint");

	/// <summary>Modern: Swiss style, white and cobalt, a geometric sans in capitals on a layout grid, black and white pictures.</summary>
	public static readonly PortalTheme Modern = Genre("modern", "Modern", dark: false,
		["#f3f4f6", "#ffffff", "#f0f1f4", "#e8eaee", "#e4e7ec", "#0d0f14", "#474c55", "#5c616b", "#d5d9e0", "#e6e9ee", "#2448d8", "#8f5300", "#c0362c"],
		display: "space", body: "ui", corners: "sharp", texture: "swiss", ornament: "block", frame: "bar", titles: "caps", effect: "none", imagery: "mono");

	/// <summary>Mystery: noir, light through blinds, typewritten case notes taped up, pictures in hard black and white.</summary>
	public static readonly PortalTheme Mystery = Genre("mystery", "Mystery", dark: true,
		["#0f1012", "#17181b", "#1d1f23", "#131416", "#0a0a0c", "#e8e4dc", "#a8a49b", "#8e8a82", "#2c2d31", "#222327", "#d9a441", "#e8b45e", "#ff8a8a"],
		display: "typewriter", body: "ui", corners: "sharp", texture: "blinds", ornament: "rule", frame: "tape", titles: "normal", effect: "bleed", imagery: "noir");

	/// <summary>Romance: blush and rose, scattered petals, an italic garamond between hearts, lace-edged cards.</summary>
	public static readonly PortalTheme Romance = Genre("romance", "Romance", dark: false,
		["#f9edf0", "#fffafb", "#f8e9ee", "#f2e0e6", "#edd4dc", "#2d1a20", "#6b4b55", "#735560", "#e6c9d3", "#f0dde3", "#b0306a", "#8f4b00", "#b3261e"],
		display: "cormorant", body: "serif", corners: "round", texture: "petals", ornament: "heart", frame: "scallop", titles: "italic", effect: "gilt", imagery: "tint");

	/// <summary>Science fiction: deep space and cyan, wide glowing capitals in brackets, scanlines and targeting corners.</summary>
	public static readonly PortalTheme ScienceFiction = Genre("science-fiction", "Science Fiction", dark: true,
		["#070b14", "#0d1422", "#121b2d", "#0a101b", "#04070d", "#dbe8ff", "#93a7c6", "#7a8eae", "#1d2a42", "#152035", "#36d6ff", "#ffc857", "#ff8aa0"],
		display: "orbitron", body: "ui", corners: "sharp", texture: "scanlines", ornament: "brackets", frame: "corners", titles: "caps", effect: "glow", imagery: "tint");

	/// <summary>Spiritual: ivory and amethyst, a mandala of light from above, a calm roman between flowers, haloed cards.</summary>
	public static readonly PortalTheme Spiritual = Genre("spiritual", "Spiritual", dark: false,
		["#f5f1f9", "#fdfbff", "#f3eef9", "#ebe5f3", "#e3dbef", "#241f2e", "#574e66", "#6a6178", "#ddd4ea", "#ebe5f3", "#6b4fa8", "#8a5300", "#b3361e"],
		display: "marcellus", body: "ui", corners: "round", texture: "mandala", ornament: "lotus", frame: "halo", titles: "normal", effect: "glow", imagery: "tint");

	public static readonly IReadOnlyList<PortalTheme> All =
		[Phosphor, Daylight, Fantasy, Historical, Horror, Modern, Mystery, Romance, ScienceFiction, Spiritual];

	private static PortalTheme Genre(string id, string name, bool dark, string[] colors, string display, string body,
		string corners, string texture, string ornament, string frame, string titles, string effect, string imagery)
	{
		var tokens = ThemeTokens.Editable.Zip(colors).ToDictionary(p => p.First, p => p.Second);
		tokens[ThemeStyles.FontDisplay] = display;
		tokens[ThemeStyles.FontBody] = body;
		tokens[ThemeStyles.Corners] = corners;
		tokens[ThemeStyles.Texture] = texture;
		tokens[ThemeStyles.Ornament] = ornament;
		tokens[ThemeStyles.Frame] = frame;
		tokens[ThemeStyles.Titles] = titles;
		tokens[ThemeStyles.Effect] = effect;
		tokens[ThemeStyles.Imagery] = imagery;
		return new PortalTheme(id, name, dark, Published: true, tokens, BuiltIn: true);
	}

	/// <summary>Quick picks for a character's accent. Each is adjusted for the theme it lands on, like any other.</summary>
	public static readonly IReadOnlyList<(string Name, string Hex)> AccentSwatches =
	[
		("Phosphor", "#00f5b7"),
		("Signal", "#5aa9ff"),
		("Violet", "#b39cff"),
		("Rose", "#ff7a9c"),
		("Amber", "#ffb454"),
		("Lime", "#a6e22e"),
		("Coral", "#ff7f50"),
		("Slate", "#9aa8b8"),
	];
}

/// <summary>
/// What the portal paints with: a theme's tokens, a character's accent laid over them, and every token derived
/// from those. <see cref="Css"/> is the stylesheet that applies it.
/// </summary>
/// <param name="Accent">The accent in use, after <see cref="AccentAdjusted"/>.</param>
/// <param name="RequestedAccent">The character's own accent as chosen, or null when the theme's is in use.</param>
/// <param name="AccentAdjusted">The chosen accent was too faint against this theme and was moved until it read.</param>
/// <param name="Tokens">Every custom property this theme sets, by name without the <c>--</c>.</param>
/// <param name="Style">The theme's <see cref="ThemeStyles"/> choices, by setting.</param>
/// <param name="Stylesheet">The theme's own stylesheet (<see cref="ThemeStylesheet"/>), or null.</param>
public sealed record ResolvedTheme(
	string ThemeId,
	string Name,
	bool Dark,
	string Accent,
	string? RequestedAccent,
	bool AccentAdjusted,
	IReadOnlyDictionary<string, string> Tokens,
	IReadOnlyDictionary<string, string> Style,
	string? Stylesheet = null)
{
	/// <summary>The tokens a character's own accent sets, laid again after a theme's stylesheet so it keeps them.</summary>
	public static readonly IReadOnlyList<string> AccentTokens =
		[ThemeTokens.Accent, "accent-dim", "accent-on", "glow", "ms-border", "ms-title", "ms-heading", "ms-bullet"];

	/// <summary>
	/// A <c>:root</c> rule that sets every token, then the theme's stylesheet. Unlayered, so it wins over
	/// <c>tokens.css</c>'s defaults. A character's own accent is set once more after the stylesheet, so a theme
	/// that sets <c>--accent</c> itself still shows each character's.
	/// </summary>
	public string Css
	{
		get
		{
			var css = new StringBuilder(":root{color-scheme:").Append(Dark ? "dark" : "light").Append(';');
			foreach (var (name, value) in Tokens)
			{
				css.Append("--").Append(name).Append(':').Append(value).Append(';');
			}

			css.Append('}');
			if (string.IsNullOrWhiteSpace(Stylesheet))
			{
				return css.ToString();
			}

			css.Append('\n').Append(Stylesheet).Append('\n');
			if (RequestedAccent is not null)
			{
				css.Append(":root{");
				foreach (var name in AccentTokens.Where(Tokens.ContainsKey))
				{
					css.Append("--").Append(name).Append(':').Append(Tokens[name]).Append(';');
				}

				css.Append('}');
			}

			return css.ToString();
		}
	}

	public string Token(string name) => Tokens[name];
}

/// <summary>Turns a theme and a character's accent into a <see cref="ResolvedTheme"/>.</summary>
public static class ThemeResolver
{
	/// <summary>
	/// The theme the character sees: its chosen theme if that is still offered, else the game's default for the
	/// browser's light or dark preference, else Phosphor.
	/// </summary>
	public static PortalTheme Pick(IReadOnlyList<PortalTheme> themes, PortalThemeDefaults defaults, bool prefersLight, string? chosenThemeId)
		=> themes.FirstOrDefault(t => t.Id == chosenThemeId)
			?? themes.FirstOrDefault(t => t.Id == defaults.For(prefersLight))
			?? themes.FirstOrDefault(t => t.Id == defaults.DarkThemeId)
			?? BuiltInThemes.Phosphor;

	public static ResolvedTheme Resolve(PortalTheme theme, string? accent = null)
	{
		var (tokens, style) = Complete(ThemeStylesheet.WithColorOverrides(theme.Tokens, theme.Stylesheet));
		var bg = ThemeColor.Parse(tokens[ThemeTokens.Background]);
		var surface = ThemeColor.Parse(tokens[ThemeTokens.Surface]);
		// A chosen accent is drawn on the page, cards, the current sidebar row and the sidebar itself.
		ThemeColor[] grounds = [bg, surface, ThemeColor.Parse(tokens[ThemeTokens.Surface2]), ThemeColor.Parse(tokens[ThemeTokens.Surface3])];

		var requested = ThemeColor.TryParse(accent, out var chosen) ? chosen : (ThemeColor?)null;
		var accentColor = requested is { } own
			? ReadableAgainst(own, theme.Dark, ThemeTokens.TextContrast, grounds)
			: ThemeColor.Parse(tokens[ThemeTokens.Accent]);
		tokens[ThemeTokens.Accent] = accentColor.Hex;

		Derive(tokens, theme.Dark, accentColor);
		foreach (var (name, value) in ThemeStyles.Css(style, ThemeColor.Parse(tokens[ThemeTokens.Text]), accentColor,
			ThemeColor.Parse(tokens[ThemeTokens.Border]), theme.Dark))
		{
			tokens[name] = value;
		}

		return new ResolvedTheme(theme.Id, theme.Name, theme.Dark, accentColor.Hex, requested?.Hex,
			requested is { } asked && asked != accentColor, tokens, style,
			string.IsNullOrWhiteSpace(theme.Stylesheet) ? null : theme.Stylesheet);
	}

	/// <summary>
	/// <paramref name="color"/> if it already reaches <paramref name="minimum"/> against every one of
	/// <paramref name="backgrounds"/>; otherwise the least move toward white (on a dark theme) or black (on a
	/// light one) that does. Hue is kept, so a character's accent still reads as theirs.
	/// </summary>
	public static ThemeColor ReadableAgainst(ThemeColor color, bool dark, double minimum, params ThemeColor[] backgrounds)
	{
		var target = dark ? ThemeColor.White : ThemeColor.Black;
		for (var step = 0; step <= 50; step++)
		{
			var candidate = color.Mix(target, step / 50.0);
			if (backgrounds.All(b => ThemeColor.Contrast(candidate, b) >= minimum))
			{
				return candidate;
			}
		}

		return target;
	}

	/// <summary>Text on an accent fill: white or a near-black of the accent's own hue, whichever reads better.</summary>
	public static ThemeColor OnAccent(ThemeColor accent)
	{
		var dark = accent.Mix(ThemeColor.Black, 0.88);
		return ThemeColor.Contrast(dark, accent) >= ThemeColor.Contrast(ThemeColor.White, accent) ? dark : ThemeColor.White;
	}

	/// <summary>
	/// Problems with a theme's tokens: a missing or malformed colour, or a style setting that is not one of its
	/// choices. A style setting may be left out. Empty when the theme can be saved.
	/// </summary>
	public static IReadOnlyList<string> Validate(IReadOnlyDictionary<string, string>? tokens)
	{
		var problems = new List<string>();
		foreach (var name in ThemeTokens.Editable)
		{
			if (tokens is null || !tokens.TryGetValue(name, out var value))
			{
				problems.Add($"Token '{name}' is missing.");
			}
			else if (!ThemeColor.TryParse(value, out _))
			{
				problems.Add($"Token '{name}' is not a #rrggbb colour: '{value}'.");
			}
		}

		if (tokens is not null)
		{
			foreach (var (name, value) in tokens)
			{
				if (ThemeStyles.Choices.TryGetValue(name, out var choices))
				{
					if (!choices.Contains(value))
					{
						problems.Add($"Style '{name}' is one of {string.Join(", ", choices)}, not '{value}'.");
					}
				}
				else if (!ThemeTokens.Editable.Contains(name))
				{
					problems.Add($"Token '{name}' is not a theme token.");
				}
			}
		}

		return problems;
	}

	/// <summary>Each of <see cref="ThemeTokens.ContrastPairs"/> with its ratio.</summary>
	public static IReadOnlyList<(string Foreground, string Background, double Ratio)> Contrasts(IReadOnlyDictionary<string, string> tokens)
		=> ThemeTokens.ContrastPairs
			.Select(p => (p.Foreground, p.Background,
				Fg: tokens.TryGetValue(p.Foreground, out var fg) && ThemeColor.TryParse(fg, out var f) ? f : (ThemeColor?)null,
				Bg: tokens.TryGetValue(p.Background, out var bg) && ThemeColor.TryParse(bg, out var b) ? b : (ThemeColor?)null))
			.Where(p => p.Fg is not null && p.Bg is not null)
			.Select(p => (p.Foreground, p.Background, ThemeColor.Contrast(p.Fg!.Value, p.Bg!.Value)))
			.ToList();

	/// <summary>
	/// The theme's colours, normalised to lower-case <c>#rrggbb</c> with Phosphor's for any it lacks, and its style
	/// choices with the defaults for any it lacks.
	/// </summary>
	public static (Dictionary<string, string> Colors, Dictionary<string, string> Style) Complete(IReadOnlyDictionary<string, string> tokens)
	{
		var colors = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var name in ThemeTokens.Editable)
		{
			colors[name] = tokens.TryGetValue(name, out var value) && ThemeColor.TryParse(value, out var color)
				? color.Hex
				: BuiltInThemes.Phosphor.Tokens[name];
		}

		var style = ThemeStyles.Keys.ToDictionary(k => k,
			k => tokens.TryGetValue(k, out var value) && ThemeStyles.IsValid(k, value) ? value : ThemeStyles.Defaults[k], StringComparer.Ordinal);
		return (colors, style);
	}

	/// <summary>The status and kind colours every theme derives, with Phosphor's values (<c>tokens.css</c>).</summary>
	public static readonly IReadOnlyList<(string Name, string Hex)> StatusColors =
	[
		("danger", "#e57373"),
		("success", "#6cde9a"),
		("info", "#5aa9ff"),
		("special", "#b39cff"),
	];

	/// <summary>The syntax colours every theme derives, with Phosphor's values (<c>tokens.css</c>).</summary>
	public static readonly IReadOnlyList<(string Name, string Hex)> SyntaxColors =
	[
		("syntax-command", "#c792ea"),
		("syntax-function", "#82aaff"),
		("syntax-substitution", "#f78c6c"),
		("syntax-dbref", "#89ddff"),
		("syntax-reference", "#c3e88d"),
		("syntax-at-command", "#ffcb6b"),
		("syntax-danger", "#ff5350"),
		("syntax-string", "#ce9178"),
		("syntax-link", "#4ec9b0"),
		("syntax-heading", "#9cdcfe"),
		("syntax-emphasis", "#dcdcaa"),
	];

	private static void Derive(Dictionary<string, string> tokens, bool dark, ThemeColor accent)
	{
		ThemeColor Get(string name) => ThemeColor.Parse(tokens[name]);
		var bg = Get(ThemeTokens.Background);
		var surface = Get(ThemeTokens.Surface);
		var surface3 = Get(ThemeTokens.Surface3);
		var rail = Get(ThemeTokens.Rail);
		var warn = Get(ThemeTokens.Warn);

		tokens["accent-dim"] = accent.Mix(ThemeColor.Black, 0.22).Hex;
		tokens["accent-on"] = OnAccent(accent).Hex;
		tokens["glow"] = accent.Triple;
		tokens["warn-tint"] = $"rgba({warn.Triple}, 0.14)";
		tokens["unread-alert"] = tokens[ThemeTokens.LinkMissing];
		tokens["mud-palette-background-gradient"] = $"radial-gradient(120% 100% at 80% -10%, {surface.Mix(bg, 0.1).Hex} 0%, {bg.Hex} 55%)";
		tokens["mud-palette-navbar-gradient"] = $"linear-gradient(90deg, {surface3.Hex} 0%, {surface3.Mix(rail, 0.5).Hex} 100%)";

		if (dark)
		{
			tokens["switch-knob-off"] = "#c3cad0";
			tokens["shadow"] = "0 1px 0 rgba(255,255,255,0.03) inset, 0 4px 16px rgba(0,0,0,0.4)";
			tokens["ooc-band-bg"] = "#2e2748";
			tokens["ooc-band-border"] = "#6a5aa3";
			tokens["ooc-icon-bg"] = "#4a3d78";
			tokens["ooc-icon-fg"] = "#d6c9ff";
		}
		else
		{
			tokens["switch-knob-off"] = tokens[ThemeTokens.TextFaint];
			tokens["shadow"] = "0 1px 0 rgba(255,255,255,0.6) inset, 0 4px 16px rgba(20,28,36,0.10)";
			tokens["ooc-band-bg"] = "#ebe7fb";
			tokens["ooc-band-border"] = "#9486d0";
			tokens["ooc-icon-bg"] = "#d8cff7";
			tokens["ooc-icon-fg"] = "#3f3370";
		}

		// Status and kind colours (deletes and denials, allows and results, notices, roles and forms), and the code
		// and syntax colours of help, the softcode console and highlighted softcode: each readable on the surfaces
		// it is drawn on, starting from Phosphor's and moved toward white or black as the theme needs.
		ThemeColor[] grounds = [bg, surface, surface3];
		foreach (var (name, hex) in StatusColors)
		{
			tokens[name] = ReadableAgainst(ThemeColor.Parse(hex), dark, ThemeTokens.TextContrast, grounds).Hex;
		}

		var codeBg = dark ? bg.Mix(ThemeColor.Black, 0.35) : surface3;
		tokens["code-bg"] = codeBg.Hex;
		tokens["code-text"] = ReadableAgainst(Get(ThemeTokens.Text), dark, ThemeTokens.TextContrast, codeBg).Hex;
		foreach (var (name, hex) in SyntaxColors)
		{
			tokens[name] = ReadableAgainst(ThemeColor.Parse(hex), dark, ThemeTokens.TextContrast, codeBg, surface).Hex;
		}

		// MarkupString's layout HTML reads these (border = primary, title and label = secondary, guide = muted,
		// bullet = tertiary), so a layout drawn in the portal follows the theme with no markup of its own.
		tokens["ms-border"] = tokens["accent-dim"];
		tokens["ms-title"] = accent.Hex;
		tokens["ms-heading"] = accent.Hex;
		tokens["ms-label"] = tokens[ThemeTokens.TextDim];
		tokens["ms-bullet"] = accent.Hex;
		tokens["ms-guide"] = tokens[ThemeTokens.Border];
		tokens["ms-muted"] = tokens[ThemeTokens.TextFaint];
	}
}
