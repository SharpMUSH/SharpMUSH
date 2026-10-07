using System.Globalization;

namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// What a theme sets beyond colour: its typefaces, corners, background texture, title ornament, card frame and
/// title lettering. Each is a choice from a fixed list, so a theme names a look and never carries CSS of its own;
/// <see cref="Css"/> turns the choices into the custom properties <c>themes.css</c> and the kit components read.
/// </summary>
public static class ThemeStyles
{
	public const string FontDisplay = "font-display";
	public const string FontBody = "font-body";
	public const string Corners = "corners";
	public const string Texture = "texture";
	public const string Ornament = "ornament";
	public const string Frame = "frame";
	public const string Titles = "titles";

	/// <summary>A typeface: the CSS stack, and the weight titles are set in (some faces have only one).</summary>
	public sealed record Face(string Stack, int TitleWeight);

	/// <summary>Faces for titles. All but <c>ui</c> are self-hosted under <c>wwwroot/fonts</c> and declared in <c>tokens.css</c>.</summary>
	public static readonly IReadOnlyDictionary<string, Face> DisplayFaces = new Dictionary<string, Face>
	{
		["ui"] = new("'Hanken Grotesk', system-ui, sans-serif", 700),
		["cinzel"] = new("'Cinzel', 'Times New Roman', serif", 700),
		["cormorant"] = new("'Cormorant Garamond', Georgia, serif", 600),
		["fell"] = new("'IM Fell English', Georgia, serif", 400),
		["playfair"] = new("'Playfair Display', Georgia, serif", 700),
		["grenze"] = new("'Grenze Gotisch', 'Times New Roman', serif", 600),
		["space"] = new("'Space Grotesk', system-ui, sans-serif", 700),
		["typewriter"] = new("'Special Elite', 'Courier New', monospace", 400),
		["orbitron"] = new("'Orbitron', system-ui, sans-serif", 700),
		["marcellus"] = new("'Marcellus', Georgia, serif", 400),
	};

	/// <summary>Faces for everything else. Kept to faces that read well small.</summary>
	public static readonly IReadOnlyDictionary<string, string> BodyFaces = new Dictionary<string, string>
	{
		["ui"] = "'Hanken Grotesk', system-ui, sans-serif",
		["serif"] = "Georgia, 'Iowan Old Style', 'Times New Roman', serif",
	};

	/// <summary>Every style setting with its choices, the first being what a theme that sets none gets.</summary>
	public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Choices = new Dictionary<string, IReadOnlyList<string>>
	{
		[FontDisplay] = [.. DisplayFaces.Keys],
		[FontBody] = [.. BodyFaces.Keys],
		[Corners] = ["soft", "sharp", "round"],
		[Texture] = ["none", "grain", "paper", "linen", "grid", "scanlines", "stars", "mist", "damask", "lace"],
		[Ornament] = ["none", "rule", "diamond", "fleuron", "star", "brackets", "cross", "heart", "lotus"],
		[Frame] = ["plain", "double", "corners", "glow", "inset"],
		[Titles] = ["normal", "caps", "italic"],
	};

	/// <summary>The settings in the order the editor lists them.</summary>
	public static readonly IReadOnlyList<string> Keys = [FontDisplay, FontBody, Corners, Texture, Ornament, Frame, Titles];

	/// <summary>Phosphor's look: what any setting a theme leaves out falls back to.</summary>
	public static readonly IReadOnlyDictionary<string, string> Defaults = Keys.ToDictionary(k => k, k => Choices[k][0]);

	public static bool IsValid(string key, string? value) => Choices.TryGetValue(key, out var choices) && value is not null && choices.Contains(value);

	/// <summary>
	/// The custom properties for <paramref name="style"/>, drawn in the theme's own colours. Values that hold CSS
	/// are built here from the fixed choices, never from text a theme supplied.
	/// </summary>
	public static Dictionary<string, string> Css(IReadOnlyDictionary<string, string> style, ThemeColor text, ThemeColor accent, ThemeColor border, bool dark)
	{
		string Pick(string key) => style.TryGetValue(key, out var v) && IsValid(key, v) ? v : Defaults[key];

		var css = new Dictionary<string, string>(StringComparer.Ordinal);
		var face = DisplayFaces[Pick(FontDisplay)];
		css["font-display"] = face.Stack;
		css["font-title-weight"] = face.TitleWeight.ToString(CultureInfo.InvariantCulture);
		css["font-ui"] = BodyFaces[Pick(FontBody)];

		(css["radius"], css["radius-lg"], css["radius-card"], css["radius-row"], css["radius-tile"]) = Pick(Corners) switch
		{
			"sharp" => ("3px", "4px", "4px", "3px", "3px"),
			"round" => ("14px", "20px", "22px", "14px", "14px"),
			_ => ("9px", "14px", "16px", "10px", "10px"),
		};

		var (texture, textureSize) = TextureLayers(Pick(Texture), text, accent, dark);
		css["texture"] = texture;
		css["texture-size"] = textureSize;

		var (before, after, underline) = Pick(Ornament) switch
		{
			"rule" => ("none", "none", Fade(accent)),
			"diamond" => (Glyph("25C7"), Glyph("25C7"), "none"),
			"fleuron" => ("none", Glyph("2766"), Fade(accent)),
			"star" => (Glyph("2726"), Glyph("2726"), "none"),
			"brackets" => (Glyph("5B"), Glyph("5D"), "none"),
			"cross" => (Glyph("2020"), Glyph("2020"), "none"),
			"heart" => ("none", Glyph("2661"), Fade(accent)),
			"lotus" => (Glyph("2741"), Glyph("2741"), "none"),
			_ => ("none", "none", "none"),
		};
		css["ornament-before"] = before;
		css["ornament-after"] = after;
		css["title-underline"] = underline;
		css["title-underline-pad"] = underline == "none" ? "0px" : "6px";

		var (ticks, borderStyle, borderWidth, shadow) = Pick(Frame) switch
		{
			"double" => (null, "double", "3px", "none"),
			"corners" => (CornerTicks(accent), "solid", "1px", "none"),
			"glow" => (null, "solid", "1px", $"0 0 0 1px {Rgba(accent, 0.22)}, 0 0 22px {Rgba(accent, 0.14)}"),
			"inset" => (null, "solid", "1px", $"inset 0 0 0 3px var(--surface), inset 0 0 0 4px {border.Hex}"),
			_ => ((string?)null, "solid", "1px", "none"),
		};
		// A card's background: the frame's corner ticks, then the page's grain where the page has one, then the surface.
		string?[] cardLayers =
		[
			ticks,
			Pick(Texture) is "paper" or "linen" or "grain" ? $"{Noise(text, dark ? 0.05 : 0.07)} 0 0 / 180px 180px" : null,
			"var(--surface)",
		];
		css["card-bg"] = string.Join(", ", cardLayers.OfType<string>());
		css["card-border-style"] = borderStyle;
		css["card-border-width"] = borderWidth;
		css["card-shadow"] = shadow;

		(css["title-transform"], css["title-tracking"], css["title-style"]) = Pick(Titles) switch
		{
			"caps" => ("uppercase", "0.08em", "normal"),
			"italic" => ("none", "0.01em", "italic"),
			_ => ("none", "normal", "normal"),
		};

		return css;
	}

	/// <summary>A CSS string of one character, by code point, so no glyph or quote is written into the stylesheet raw.</summary>
	private static string Glyph(string codePoint) => $"\"\\{codePoint}\"";

	private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

	private static string Rgba(ThemeColor c, double alpha) => $"rgba({c.Triple},{F(alpha)})";

	private static string Fade(ThemeColor accent) => $"linear-gradient(90deg, {accent.Hex}, {Rgba(accent, 0)})";

	/// <summary>Fractal noise as an SVG data URI: grain and paper.</summary>
	private static string Noise(ThemeColor ink, double alpha)
	{
		var color = $"0 0 0 0 {F(ink.R / 255.0)} 0 0 0 0 {F(ink.G / 255.0)} 0 0 0 0 {F(ink.B / 255.0)} 0 0 0 {F(alpha)} 0";
		return "url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='180' height='180'%3E"
			+ "%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='3' stitchTiles='stitch'/%3E"
			+ $"%3CfeColorMatrix values='{color}'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E\")";
	}

	/// <summary>Accent ticks on a card's four corners.</summary>
	private static string CornerTicks(ThemeColor accent)
	{
		var bar = $"linear-gradient({accent.Hex}, {accent.Hex})";
		return string.Join(", ",
			$"{bar} left 6px top 6px / 14px 2px no-repeat", $"{bar} left 6px top 6px / 2px 14px no-repeat",
			$"{bar} right 6px top 6px / 14px 2px no-repeat", $"{bar} right 6px top 6px / 2px 14px no-repeat",
			$"{bar} left 6px bottom 6px / 14px 2px no-repeat", $"{bar} left 6px bottom 6px / 2px 14px no-repeat",
			$"{bar} right 6px bottom 6px / 14px 2px no-repeat", $"{bar} right 6px bottom 6px / 2px 14px no-repeat");
	}

	/// <summary>The page background's texture layers and their sizes, laid over the theme's own gradient.</summary>
	private static (string Layers, string Sizes) TextureLayers(string texture, ThemeColor ink, ThemeColor accent, bool dark)
	{
		var faint = dark ? 0.035 : 0.05;
		return texture switch
		{
			"grain" => (Noise(ink, dark ? 0.06 : 0.08), "180px 180px"),
			"paper" => ($"radial-gradient(ellipse at 50% 40%, {Rgba(ink, 0)} 55%, {Rgba(ink, dark ? 0.18 : 0.10)} 100%), {Noise(ink, dark ? 0.07 : 0.10)}",
				"100% 100%, 180px 180px"),
			"linen" => ($"repeating-linear-gradient(0deg, {Rgba(ink, faint)} 0 1px, {Rgba(ink, 0)} 1px 3px), "
				+ $"repeating-linear-gradient(90deg, {Rgba(ink, faint)} 0 1px, {Rgba(ink, 0)} 1px 3px)", "auto, auto"),
			"grid" => ($"linear-gradient({Rgba(ink, faint * 1.4)} 1px, {Rgba(ink, 0)} 1px), "
				+ $"linear-gradient(90deg, {Rgba(ink, faint * 1.4)} 1px, {Rgba(ink, 0)} 1px)", "28px 28px, 28px 28px"),
			"scanlines" => ($"repeating-linear-gradient(0deg, {Rgba(accent, 0.045)} 0 1px, {Rgba(accent, 0)} 1px 4px), "
				+ $"radial-gradient(90% 60% at 50% -10%, {Rgba(accent, 0.12)}, {Rgba(accent, 0)} 70%)", "auto, 100% 100%"),
			"stars" => ($"radial-gradient(1px 1px at 23px 41px, {Rgba(ink, 0.55)}, {Rgba(ink, 0)}), "
				+ $"radial-gradient(1px 1px at 97px 13px, {Rgba(ink, 0.4)}, {Rgba(ink, 0)}), "
				+ $"radial-gradient(1.5px 1.5px at 151px 117px, {Rgba(accent, 0.6)}, {Rgba(accent, 0)}), "
				+ $"radial-gradient(1px 1px at 61px 163px, {Rgba(ink, 0.35)}, {Rgba(ink, 0)})",
				"190px 190px, 190px 190px, 190px 190px, 190px 190px"),
			"mist" => ($"radial-gradient(60% 45% at 15% 5%, {Rgba(accent, dark ? 0.13 : 0.10)}, {Rgba(accent, 0)} 70%), "
				+ $"radial-gradient(55% 50% at 95% 95%, {Rgba(ink, dark ? 0.07 : 0.05)}, {Rgba(ink, 0)} 70%)", "100% 100%, 100% 100%"),
			"damask" => ($"radial-gradient(circle at 50% 50%, {Rgba(accent, 0.07)} 0 2px, {Rgba(accent, 0)} 3px), "
				+ $"repeating-linear-gradient(45deg, {Rgba(accent, 0.03)} 0 1px, {Rgba(accent, 0)} 1px 14px), "
				+ $"repeating-linear-gradient(-45deg, {Rgba(accent, 0.03)} 0 1px, {Rgba(accent, 0)} 1px 14px)", "20px 20px, auto, auto"),
			"lace" => ($"radial-gradient(circle at 0 0, {Rgba(accent, 0)} 9px, {Rgba(accent, 0.09)} 10px, {Rgba(accent, 0)} 11px), "
				+ $"radial-gradient(circle at 50% 50%, {Rgba(accent, 0.08)} 0 1.5px, {Rgba(accent, 0)} 2px)", "20px 20px, 20px 20px"),
			_ => ("none", "auto"),
		};
	}
}
