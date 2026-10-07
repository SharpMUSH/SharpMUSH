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
		(TextDim, Surface),
		(TextFaint, Surface),
		(Accent, Background),
		(Accent, Surface),
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

	public static readonly IReadOnlyList<PortalTheme> All = [Phosphor, Daylight];

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
public sealed record ResolvedTheme(
	string ThemeId,
	string Name,
	bool Dark,
	string Accent,
	string? RequestedAccent,
	bool AccentAdjusted,
	IReadOnlyDictionary<string, string> Tokens)
{
	/// <summary>A <c>:root</c> rule that sets every token. Unlayered, so it wins over <c>tokens.css</c>'s defaults.</summary>
	public string Css
	{
		get
		{
			var css = new StringBuilder(":root{color-scheme:").Append(Dark ? "dark" : "light").Append(';');
			foreach (var (name, value) in Tokens)
			{
				css.Append("--").Append(name).Append(':').Append(value).Append(';');
			}

			return css.Append('}').ToString();
		}
	}

	public string Token(string name) => Tokens[name];
}

/// <summary>Turns a theme and a character's accent into a <see cref="ResolvedTheme"/>.</summary>
public static class ThemeResolver
{
	/// <summary>
	/// The theme the character sees: its chosen theme if that is still offered, else the default, else Phosphor.
	/// </summary>
	public static PortalTheme Pick(IReadOnlyList<PortalTheme> themes, string? defaultThemeId, string? chosenThemeId)
		=> themes.FirstOrDefault(t => t.Id == chosenThemeId)
			?? themes.FirstOrDefault(t => t.Id == defaultThemeId)
			?? BuiltInThemes.Phosphor;

	public static ResolvedTheme Resolve(PortalTheme theme, string? accent = null)
	{
		var tokens = Complete(theme.Tokens);
		var bg = ThemeColor.Parse(tokens[ThemeTokens.Background]);
		var surface = ThemeColor.Parse(tokens[ThemeTokens.Surface]);

		var requested = ThemeColor.TryParse(accent, out var chosen) ? chosen : (ThemeColor?)null;
		var accentColor = requested is { } own
			? ReadableAgainst(own, theme.Dark, ThemeTokens.TextContrast, bg, surface)
			: ThemeColor.Parse(tokens[ThemeTokens.Accent]);
		tokens[ThemeTokens.Accent] = accentColor.Hex;

		Derive(tokens, theme.Dark, accentColor);

		return new ResolvedTheme(theme.Id, theme.Name, theme.Dark, accentColor.Hex, requested?.Hex,
			requested is { } asked && asked != accentColor, tokens);
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

	/// <summary>Problems with a theme's tokens: a missing or malformed colour. Empty when it can be saved.</summary>
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
			problems.AddRange(tokens.Keys.Where(k => !ThemeTokens.Editable.Contains(k)).Select(k => $"Token '{k}' is not a theme token."));
		}

		return problems;
	}

	/// <summary>Each of <see cref="ThemeTokens.ContrastPairs"/> with its ratio.</summary>
	public static IReadOnlyList<(string Foreground, string Background, double Ratio)> Contrasts(IReadOnlyDictionary<string, string> tokens)
		=> ThemeTokens.ContrastPairs
			.Where(p => tokens.ContainsKey(p.Foreground) && tokens.ContainsKey(p.Background)
				&& ThemeColor.TryParse(tokens[p.Foreground], out _) && ThemeColor.TryParse(tokens[p.Background], out _))
			.Select(p => (p.Foreground, p.Background,
				ThemeColor.Contrast(ThemeColor.Parse(tokens[p.Foreground]), ThemeColor.Parse(tokens[p.Background]))))
			.ToList();

	/// <summary>The theme's tokens, normalised to lower-case <c>#rrggbb</c>, with Phosphor's for any it lacks.</summary>
	private static Dictionary<string, string> Complete(IReadOnlyDictionary<string, string> tokens)
	{
		var complete = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var name in ThemeTokens.Editable)
		{
			complete[name] = tokens.TryGetValue(name, out var value) && ThemeColor.TryParse(value, out var color)
				? color.Hex
				: BuiltInThemes.Phosphor.Tokens[name];
		}

		return complete;
	}

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
