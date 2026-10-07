using System.Globalization;

namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// What a theme sets beyond colour: its typefaces, corners, background texture, title ornament, card frame,
/// title lettering and effect, and the tone pictures take. Each is a choice from a fixed list, so a theme names a look and never carries CSS of its own;
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
	public const string Effect = "effect";
	public const string Imagery = "imagery";

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
		[Texture] = ["none", "grain", "paper", "linen", "grid", "scanlines", "stars", "mist", "damask", "lace",
			"velvet", "parchment", "foxing", "blood", "blinds", "petals", "mandala", "swiss"],
		[Ornament] = ["none", "rule", "diamond", "fleuron", "star", "brackets", "cross", "heart", "lotus", "deco", "double", "block"],
		[Frame] = ["plain", "double", "corners", "glow", "inset", "filigree", "engraved", "drip", "tape", "bar", "deco", "halo", "scallop"],
		[Titles] = ["normal", "caps", "italic"],
		[Effect] = ["none", "glow", "gilt", "emboss", "ember", "bleed"],
		[Imagery] = ["natural", "tint", "sepia", "mono", "noir"],
	};

	/// <summary>The settings in the order the editor lists them.</summary>
	public static readonly IReadOnlyList<string> Keys = [FontDisplay, FontBody, Corners, Texture, Ornament, Frame, Titles, Effect, Imagery];

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
			"fleuron" => (Glyph("2619"), Glyph("2767"), Fade(accent)),
			"star" => (Glyph("2726"), Glyph("2726"), "none"),
			"brackets" => (Glyph("5B"), Glyph("5D"), "none"),
			"cross" => (Glyph("2020"), Glyph("2020"), "none"),
			"heart" => (Glyph("2661"), Glyph("2661"), Fade(accent)),
			"lotus" => (Glyph("2741"), Glyph("2741"), "none"),
			"deco" => (Glyph("2756"), Glyph("2756"), "none"),
			"double" => ("none", "none", $"linear-gradient(180deg, {accent.Hex} 0 2px, {Rgba(accent, 0)} 2px 4px, {accent.Hex} 4px 5px)"),
			"block" => (Glyph("25A0"), "none", "none"),
			_ => ("none", "none", "none"),
		};
		css["ornament-before"] = before;
		css["ornament-after"] = after;
		css["title-underline"] = underline;
		css["title-underline-height"] = Pick(Ornament) == "double" ? "5px" : "2px";
		css["title-underline-pad"] = underline == "none" ? "0px" : Pick(Ornament) == "double" ? "9px" : "6px";

		var (marks, borderStyle, borderWidth, shadow) = Pick(Frame) switch
		{
			"double" => (null, "double", "3px", "none"),
			"corners" => (CornerTicks(accent), "solid", "1px", "none"),
			"glow" => (null, "solid", "1px", $"0 0 0 1px {Rgba(accent, 0.22)}, 0 0 22px {Rgba(accent, 0.14)}"),
			"inset" => (null, "solid", "1px", $"inset 0 0 0 3px var(--surface), inset 0 0 0 4px {border.Hex}"),
			"filigree" => (Corners4(Filigree(accent), 30, 2), "solid", "1px", "none"),
			"engraved" => (null, "solid", "2px", $"inset 0 0 0 3px var(--surface), inset 0 0 0 4px {Rgba(text, 0.45)}"),
			"drip" => ($"{Drips(accent)} left top / 120px 14px repeat-x", "solid", "1px", $"0 0 18px {Rgba(accent, 0.10)}"),
			"tape" => (Corners4(Tape(dark), 40, 0, mirror: true, topOnly: true), "solid", "1px", dark ? "0 8px 18px rgba(0,0,0,0.45)" : "0 6px 14px rgba(0,0,0,0.10)"),
			"bar" => ($"linear-gradient({accent.Hex}, {accent.Hex}) left top / 5px 100% no-repeat", "solid", "1px", "none"),
			"deco" => (Corners4(Deco(accent), 28, 3), "solid", "1px", $"0 0 0 1px {Rgba(accent, 0.18)}"),
			"halo" => ($"linear-gradient(90deg, {Rgba(accent, 0)}, {Rgba(accent, 0.7)}, {Rgba(accent, 0)}) center top / 60% 2px no-repeat, "
				+ $"radial-gradient(90% 60% at 50% 0%, {Rgba(accent, 0.16)}, {Rgba(accent, 0)} 70%)", "solid", "1px", $"0 8px 26px {Rgba(accent, 0.12)}"),
			"scallop" => ($"radial-gradient(circle at 9px -2px, {Rgba(accent, 0)} 0 7px, {Rgba(accent, 0.45)} 7.5px 8.5px, {Rgba(accent, 0)} 9px) left top / 18px 10px repeat-x, "
				+ $"radial-gradient(circle at 9px 10px, {Rgba(accent, 0.5)} 0 1.2px, {Rgba(accent, 0)} 1.8px) left top / 18px 14px repeat-x, "
				+ $"radial-gradient(circle at 9px 12px, {Rgba(accent, 0)} 0 7px, {Rgba(accent, 0.45)} 7.5px 8.5px, {Rgba(accent, 0)} 9px) left bottom / 18px 10px repeat-x",
				"solid", "1px", $"0 4px 18px {Rgba(accent, 0.10)}"),
			_ => ((string?)null, "solid", "1px", "none"),
		};
		// A card's background: the frame's marks, then the page's grain where the page has one, then the surface.
		string?[] cardLayers =
		[
			marks,
			Pick(Texture) is "paper" or "linen" or "grain" or "parchment" or "foxing" ? $"{Noise(text, dark ? 0.05 : 0.07)} 0 0 / 180px 180px" : null,
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

		(css["title-color"], css["title-shadow"]) = Pick(Effect) switch
		{
			"glow" => ("var(--text)", $"0 0 2px {Rgba(accent, 0.45)}, 0 0 12px {Rgba(accent, 0.55)}"),
			"gilt" => (accent.Hex, dark ? $"0 1px 0 rgba(0,0,0,0.5), 0 0 10px {Rgba(accent, 0.22)}" : "0 1px 0 rgba(255,255,255,0.7)"),
			"emboss" => ("var(--text)", dark ? "0 1px 0 rgba(255,255,255,0.08), 0 -1px 0 rgba(0,0,0,0.6)" : "0 1px 0 rgba(255,255,255,0.9), 0 -1px 0 rgba(0,0,0,0.12)"),
			"ember" => ("var(--text)", $"0 1px 0 rgba(0,0,0,0.7), 0 0 10px {Rgba(accent, 0.5)}"),
			"bleed" => ("var(--text)", $"0 0 1px {Rgba(text, 0.75)}, 0.6px 0.6px 0 {Rgba(text, 0.3)}"),
			_ => ("var(--text)", "none"),
		};

		css["image-filter"] = Pick(Imagery) switch
		{
			"tint" => $"grayscale(1) sepia(1) hue-rotate({F(Hue(accent) - 38)}deg) saturate(2.4) brightness({(dark ? "0.85" : "1")})",
			"sepia" => "sepia(0.85) contrast(1.05) brightness(0.95)",
			"mono" => "grayscale(1) contrast(1.1)",
			"noir" => "grayscale(1) contrast(1.5) brightness(0.85)",
			_ => "none",
		};

		return css;
	}

	/// <summary>A CSS string of one character, by code point, so no glyph or quote is written into the stylesheet raw.</summary>
	private static string Glyph(string codePoint) => $"\"\\{codePoint}\"";

	private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

	private static string Rgba(ThemeColor c, double alpha) => $"rgba({c.Triple},{F(alpha)})";

	private static string Fade(ThemeColor accent) => $"linear-gradient(90deg, {accent.Hex}, {Rgba(accent, 0)})";

	/// <summary>Fractal noise as an SVG data URI: grain and paper, or at a low frequency, stains and sheen.</summary>
	private static string Noise(ThemeColor ink, double alpha, double frequency = 0.85, int size = 180)
	{
		var color = $"0 0 0 0 {F(ink.R / 255.0)} 0 0 0 0 {F(ink.G / 255.0)} 0 0 0 0 {F(ink.B / 255.0)} 0 0 0 {F(alpha)} 0";
		return Svg($"<filter id='n'><feTurbulence type='fractalNoise' baseFrequency='{F(frequency)}' numOctaves='3' stitchTiles='stitch'/>"
			+ $"<feColorMatrix values='{color}'/></filter><rect width='100%' height='100%' filter='url(#n)'/>", size, size);
	}

	/// <summary>An SVG drawing as a data URI. The markup is ours, written with single quotes.</summary>
	private static string Svg(string body, int width, int height, string? viewBox = null, string fit = "none")
	{
		var svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}'"
			+ (viewBox is null ? "" : $" viewBox='{viewBox}' preserveAspectRatio='{fit}'") + $">{body}</svg>";
		return $"url(\"data:image/svg+xml,{svg.Replace("%", "%25").Replace("<", "%3C").Replace(">", "%3E").Replace("#", "%23")}\")";
	}

	/// <summary>The hue of a colour in degrees, for tinting pictures toward it.</summary>
	private static double Hue(ThemeColor c)
	{
		double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
		double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
		if (d == 0) return 0;
		var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
		return Math.Round((h * 60 + 360) % 360);
	}

	/// <summary>
	/// A corner drawing (drawn for the top-left) placed on the corners of a card, turned for each, or with
	/// <paramref name="mirror"/> flipped instead. <paramref name="topOnly"/> keeps it to the two top corners.
	/// </summary>
	private static string Corners4(string drawing, int size, int inset, bool mirror = false, bool topOnly = false)
	{
		var c = F(size / 2.0);
		string[] turns = mirror
			? ["", $"translate({size} 0) scale(-1 1)", $"translate({size} {size}) scale(-1 -1)", $"translate(0 {size}) scale(1 -1)"]
			: ["", $"rotate(90 {c} {c})", $"rotate(180 {c} {c})", $"rotate(270 {c} {c})"];
		string[] places = [$"left {inset}px top {inset}px", $"right {inset}px top {inset}px", $"right {inset}px bottom {inset}px", $"left {inset}px bottom {inset}px"];
		return string.Join(", ", turns.Zip(places, (turn, place) =>
			$"{Svg($"<g transform='{turn}'>{drawing}</g>", size, size)} {place} / {size}px {size}px no-repeat").Take(topOnly ? 2 : 4));
	}

	/// <summary>A scrolled flourish for a corner, kept to the card's edge.</summary>
	private static string Filigree(ThemeColor accent) =>
		$"<g fill='none' stroke='{accent.Hex}' stroke-width='1.3' stroke-linecap='round'>"
		+ "<path d='M3 28V9C3 5.5 5.5 3 9 3H28'/><path d='M3 22C3 17 9 17 9 21C9 24 6 24 6 22'/><path d='M22 3C17 3 17 9 21 9C24 9 24 6 22 6'/></g>"
		+ $"<circle cx='7' cy='7' r='1.6' fill='{accent.Hex}'/>";

	/// <summary>Art deco: three stepped rules ending in a diamond.</summary>
	private static string Deco(ThemeColor accent) =>
		$"<g fill='none' stroke='{accent.Hex}' stroke-width='1.1'><path d='M1 27V1H27'/><path d='M4 20V4H20'/><path d='M7 13V7H13'/></g>"
		+ $"<rect x='24' y='-1.5' width='3' height='3' fill='{accent.Hex}' transform='rotate(45 25.5 0)'/>"
		+ $"<rect x='-1.5' y='24' width='3' height='3' fill='{accent.Hex}' transform='rotate(45 0 25.5)'/>";

	/// <summary>A strip of tape across a corner, as on a photograph pinned to a board.</summary>
	private static string Tape(bool dark) =>
		$"<rect x='-20' y='9' width='70' height='14' fill='{(dark ? "rgb(226,214,186)" : "rgb(214,196,150)")}' fill-opacity='{(dark ? "0.28" : "0.45")}' transform='rotate(-45 15 16)'/>";

	/// <summary>Petals scattered at different turns over one tile.</summary>
	private static string Petals(ThemeColor accent)
	{
		(int X, int Y, int Turn, double Scale, double Alpha)[] petals =
			[(40, 60, 20, 1, 0.20), (190, 34, 140, 0.8, 0.16), (282, 150, 75, 1.1, 0.18), (110, 200, 250, 0.9, 0.15), (240, 290, 320, 0.75, 0.17), (30, 300, 190, 0.7, 0.13)];
		var body = string.Concat(petals.Select(p =>
			$"<path transform='translate({p.X} {p.Y}) rotate({p.Turn}) scale({F(p.Scale)})' fill='{accent.Hex}' fill-opacity='{F(p.Alpha)}'"
			+ " d='M0 0C4 -9 15 -11 22 -4C24 -2 24 2 22 4C15 11 4 9 0 0Z'/>"));
		return Svg(body, 340, 340);
	}

	/// <summary>A flower of life inside rings and a ring of small circles, centred near the top of the page.</summary>
	private static string Mandala(ThemeColor accent)
	{
		var lines = new System.Text.StringBuilder();
		foreach (var r in new[] { 120, 240, 330, 360, 480, 620 })
		{
			lines.Append($"<circle r='{r}'/>");
		}
		for (var i = 0; i < 6; i++)
		{
			var a = i * Math.PI / 3;
			lines.Append($"<circle cx='{F(120 * Math.Cos(a))}' cy='{F(120 * Math.Sin(a))}' r='120'/>");
		}
		for (var i = 0; i < 24; i++)
		{
			var a = i * Math.PI / 12;
			lines.Append($"<circle cx='{F(285 * Math.Cos(a))}' cy='{F(285 * Math.Sin(a))}' r='45'/>");
			lines.Append($"<path d='M{F(360 * Math.Cos(a))} {F(360 * Math.Sin(a))}L{F(480 * Math.Cos(a))} {F(480 * Math.Sin(a))}'/>");
		}
		var body = $"<g transform='translate(600 110)' fill='none' stroke='{accent.Hex}' stroke-opacity='0.16' stroke-width='1.2'>{lines}</g>";
		return Svg(body, 1200, 800, "0 0 1200 800", "xMidYMin slice");
	}

	/// <summary>Drips running down from a card's top edge.</summary>
	private static string Drips(ThemeColor accent) => Svg(
		$"<path fill='{accent.Hex}' fill-opacity='0.85' d='M0 0H120V3C110 3 109 4 109 8C109 11 106 11 106 8C106 4 104 3 92 3C88 3 87 5 87 12C87 21 81 21 81 12C81 5 80 3 62 3"
		+ "C60 3 59 4 59 6C59 8 57 8 57 6C57 4 56 3 42 3C38 3 37 6 37 14C37 24 31 24 31 14C31 6 30 3 0 3Z'/>", 120, 14, "0 0 120 26");
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
			// Crushed velvet: a sheen from the top left, a mottled nap, and fine grain.
			"velvet" => ($"radial-gradient(80% 60% at 15% 0%, {Rgba(accent, 0.16)}, {Rgba(accent, 0)} 65%), "
				+ $"radial-gradient(70% 70% at 100% 100%, {Rgba(ink, 0.05)}, {Rgba(ink, 0)} 70%), "
				+ $"{Noise(accent, 0.16, 0.012, 600)}, {Noise(ink, 0.05)}", "100% 100%, 100% 100%, 100% 100%, 180px 180px"),
			// Parchment: edges browned as if near a flame, stains, and fibres.
			"parchment" => ($"radial-gradient(ellipse 75% 70% at 50% 45%, {Rgba(ink, 0)} 50%, {Rgba(accent, dark ? 0.2 : 0.16)} 85%, {Rgba(ink, dark ? 0.3 : 0.24)} 100%), "
				+ $"{Noise(ink, dark ? 0.12 : 0.16, 0.018, 500)}, {Noise(ink, dark ? 0.07 : 0.10)}", "100% 100%, 100% 100%, 180px 180px"),
			// Foxing: the brown spots of an old book, on a faint weave, browning at the edges.
			"foxing" => ($"radial-gradient(ellipse at 50% 45%, {Rgba(ink, 0)} 55%, {Rgba(ink, dark ? 0.2 : 0.14)} 100%), "
				+ $"radial-gradient(circle at 40px 70px, {Rgba(accent, 0.14)} 0 2px, {Rgba(accent, 0)} 9px), "
				+ $"radial-gradient(circle at 210px 30px, {Rgba(accent, 0.10)} 0 3px, {Rgba(accent, 0)} 12px), "
				+ $"radial-gradient(circle at 270px 240px, {Rgba(accent, 0.12)} 0 1.5px, {Rgba(accent, 0)} 7px), "
				+ $"radial-gradient(circle at 120px 280px, {Rgba(accent, 0.09)} 0 4px, {Rgba(accent, 0)} 14px), "
				+ $"{Noise(ink, dark ? 0.06 : 0.09)}, "
				+ $"repeating-linear-gradient(0deg, {Rgba(ink, faint)} 0 1px, {Rgba(ink, 0)} 1px 3px)",
				"100% 100%, 317px 289px, 411px 373px, 523px 467px, 389px 541px, 180px 180px, auto"),
			// Blood: a red pooling from below and the corner, stained and grainy.
			"blood" => ($"radial-gradient(120% 70% at 50% 115%, {Rgba(accent, 0.24)}, {Rgba(accent, 0)} 60%), "
				+ $"radial-gradient(60% 50% at 0% 0%, {Rgba(accent, 0.12)}, {Rgba(accent, 0)} 70%), "
				+ $"{Noise(accent, 0.14, 0.02, 400)}, {Noise(ink, 0.06)}", "100% 100%, 100% 100%, 100% 100%, 180px 180px"),
			// Light through venetian blinds, falling off into the dark away from the window.
			"blinds" => ($"radial-gradient(75% 90% at 90% 0%, rgba(0,0,0,0) 25%, rgba(0,0,0,{(dark ? "0.6" : "0.12")}) 100%), "
				+ $"repeating-linear-gradient(-32deg, {Rgba(ink, 0)} 0 34px, {Rgba(ink, dark ? 0.075 : 0.06)} 34px 58px), "
				+ $"{Noise(ink, dark ? 0.06 : 0.08)}", "100% 100%, auto, 180px 180px"),
			// Rose petals drifting across the page, under a blush from above.
			"petals" => ($"radial-gradient(60% 40% at 50% 0%, {Rgba(accent, 0.12)}, {Rgba(accent, 0)} 70%), {Petals(accent)}", "100% 100%, 340px 340px"),
			// A mandala drawn in fine lines, rising from the top of the page in a soft light.
			"mandala" => ($"radial-gradient(50% 45% at 50% 0%, {Rgba(accent, 0.14)}, {Rgba(accent, 0)} 75%), {Mandala(accent)}", "100% 100%, 100% 100%"),
			// A Swiss layout grid: major lines in the accent over a fine grid.
			"swiss" => ($"linear-gradient({Rgba(accent, 0.14)} 1px, {Rgba(accent, 0)} 1px), "
				+ $"linear-gradient(90deg, {Rgba(accent, 0.14)} 1px, {Rgba(accent, 0)} 1px), "
				+ $"linear-gradient({Rgba(ink, faint * 1.2)} 1px, {Rgba(ink, 0)} 1px), "
				+ $"linear-gradient(90deg, {Rgba(ink, faint * 1.2)} 1px, {Rgba(ink, 0)} 1px)", "168px 168px, 168px 168px, 28px 28px, 28px 28px"),
			_ => ("none", "auto"),
		};
	}
}
