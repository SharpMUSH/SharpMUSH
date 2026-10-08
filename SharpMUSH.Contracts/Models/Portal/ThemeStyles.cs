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
		["fredoka"] = new("'Fredoka', 'Trebuchet MS', system-ui, sans-serif", 700),
		["dancing"] = new("'Dancing Script', 'Brush Script MT', cursive", 700),
		["mochiy"] = new("'Mochiy Pop One', 'Arial Rounded MT Bold', system-ui, sans-serif", 400),
		["patrick"] = new("'Patrick Hand', 'Comic Sans MS', system-ui, sans-serif", 400),
		["dela"] = new("'Dela Gothic One', 'Arial Black', sans-serif", 400),
		["graduate"] = new("'Graduate', Rockwell, serif", 400),
		["saira-stencil"] = new("'Saira Stencil One', 'Arial Narrow', sans-serif", 400),
		["pixelify"] = new("'Pixelify Sans', 'Courier New', monospace", 700),
		["yuji"] = new("'Yuji Syuku', Georgia, serif", 400),
		["bangers"] = new("'Bangers', Impact, sans-serif", 400),
		["lilita"] = new("'Lilita One', 'Arial Rounded MT Bold', sans-serif", 400),
		["titan"] = new("'Titan One', 'Arial Black', sans-serif", 400),
		["chakra"] = new("'Chakra Petch', system-ui, sans-serif", 600),
		["monoton"] = new("'Monoton', system-ui, sans-serif", 400),
		["michroma"] = new("'Michroma', system-ui, sans-serif", 400),
		["antonio"] = new("'Antonio', 'Arial Narrow', sans-serif", 700),
		["oxanium"] = new("'Oxanium', 'Arial Black', system-ui, sans-serif", 800),
		["russo"] = new("'Russo One', 'Arial Black', Impact, sans-serif", 400),
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
		[Corners] = ["soft", "sharp", "round", "candy", "razor", "pill"],
		[Texture] = ["none", "grain", "paper", "linen", "grid", "scanlines", "stars", "mist", "damask", "lace",
			"velvet", "parchment", "foxing", "blood", "blinds", "petals", "mandala", "swiss",
			"sparkles", "bubbles", "spotlights", "notebook",
			"speedlines", "court", "hazard", "sigil",
			"sumi", "benday", "filmgrain", "starburst",
			"rain", "outrun", "galaxy", "readout",
			"armor-panels",
			"sunburst-rays",
		],
		[Ornament] = ["none", "rule", "diamond", "fleuron", "star", "brackets", "cross", "heart", "lotus", "deco", "double", "block",
			"twinkle", "blossom", "encore", "doodle",
			"exclaim", "varsity", "warning", "cursor",
			"hanko", "pow", "vaudeville", "bolt",
			"slash", "sunset", "insignia", "segments",
			"tricolor",
			"battlecry",
		],
		[Frame] = ["plain", "double", "corners", "glow", "inset", "filigree", "engraved", "drip", "tape", "bar", "deco", "halo", "scallop",
			"ribbon", "bloom", "stage", "washi",
			"panel", "jersey", "chamfer", "status",
			"shoji", "inked", "titlecard", "chunky",
			"glitch", "neon", "hull", "elbow",
			"plating",
			"chrome-armor",
		],
		[Titles] = ["normal", "caps", "italic", "slant", "wide"],
		[Effect] = ["none", "glow", "gilt", "emboss", "ember", "bleed",
			"shine", "dreamy", "lightstick", "pencil",
			"inked", "varsity", "stencil", "pixel",
			"lantern", "inkpop", "cartoon", "toybox",
			"rgbsplit", "retro", "console",
			"decal",
			"blazing",
		],
		[Imagery] = ["natural", "tint", "sepia", "mono", "noir",
			"pastel", "airy", "spotlit", "sunny",
			"ink", "floodlit", "steel", "vivid",
			"indigo", "print", "film", "cel",
			"neon", "dusk", "warm",
			"hangar",
			"hotblooded",
		],
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
			"candy" => ("16px", "24px", "26px", "999px", "18px"),
			"razor" => ("0px", "0px", "0px", "0px", "0px"),
			"pill" => ("12px", "20px", "24px", "999px", "16px"),
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
			"twinkle" => (Glyph("2727"), Glyph("2727"), $"linear-gradient(90deg, {accent.Hex}, {AnimeLavender.Hex} 55%, {Rgba(AnimeLavender, 0)})"),
			"blossom" => (Glyph("2740"), Glyph("2740"), $"repeating-linear-gradient(90deg, {accent.Hex} 0 2px, {Rgba(accent, 0)} 2px 5px)"),
			"encore" => (Glyph("2605"), Glyph("2605"), $"linear-gradient(90deg, {accent.Hex}, {IdolCyan.Hex} 50%, {IdolLemon.Hex})"),
			"doodle" => (Glyph("270E"), "none", $"repeating-linear-gradient(90deg, {accent.Hex} 0 9px, {Rgba(accent, 0)} 9px 13px)"),
			"exclaim" => ("none", Glyph("21\\21"), $"linear-gradient(90deg, {accent.Hex} 0 70%, {Rgba(accent, 0)} 70%)"),
			"varsity" => (Glyph("2605"), Glyph("2605"), "none"),
			"warning" => (Glyph("25E2"), Glyph("25E3"), "none"),
			"cursor" => (Glyph("25B6"), "none", Fade(accent)),
			"hanko" => ("none", Glyph("25A3"), SumiBrush(accent)),
			"pow" => (Glyph("2739"), Glyph("2739"), "none"),
			"vaudeville" => (Glyph("2605"), Glyph("2605"), $"linear-gradient({text.Hex}, {text.Hex})"),
			"bolt" => (Glyph("21AF"), Glyph("2734"), $"linear-gradient(90deg, {ToyMagenta.Hex} 0 33%, {accent.Hex} 33% 66%, {ToyCyan.Hex} 66%)"),
			"slash" => ("\"//\"", "none", $"linear-gradient(90deg, {accent.Hex}, {Rgba(NeonCyan, 0.9)} 55%, {Rgba(NeonCyan, 0)})"),
			"sunset" => ("none", "none", $"linear-gradient(90deg, {SunGold.Hex}, {SunOrange.Hex} 35%, {accent.Hex} 70%, {Rgba(accent, 0)})"),
			"insignia" => ("\"\\2501\\25C6\"", "\"\\25C6\\2501\"", Fade(accent)),
			"segments" => (Glyph("25AC"), "none", $"linear-gradient(90deg, {accent.Hex} 0 46%, {Rgba(accent, 0)} 46% 49%, {ConsoleLavender.Hex} 49% 70%, "
				+ $"{Rgba(accent, 0)} 70% 73%, {ConsolePeach.Hex} 73% 86%, {Rgba(accent, 0)} 86% 89%, {ConsolePeriwinkle.Hex} 89%)"),
			"tricolor" => (Glyph("25E5"), "none", $"linear-gradient(90deg, {accent.Hex} 0 58%, {Rgba(accent, 0)} 58% 61%, {RealRobotRed.Hex} 61% 76%, "
				+ $"{Rgba(accent, 0)} 76% 79%, {RealRobotYellow.Hex} 79% 90%, {Rgba(accent, 0)} 90%)"),
			"battlecry" => (Glyph("BB"), Glyph("AB"), $"linear-gradient(90deg, {SuperRobotGold.Hex} 0 34%, {SuperRobotOrange.Hex} 34% 62%, {SuperRobotCrimson.Hex} 62% 86%, {Rgba(SuperRobotCrimson, 0)})"),
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
			"filigree" => (Corners4(Filigree(accent), 30, 2, shown: 24), "solid", "1px", "none"),
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
			"ribbon" => ($"{Bow(accent)} right 8px top 2px / 66px 30px no-repeat, "
				+ $"linear-gradient(90deg, {Rgba(accent, 0)}, {Rgba(accent, 0.35)} 40%, {Rgba(AnimeLavender, 0.45)}) left bottom / 100% 3px no-repeat",
				"solid", "2px", $"0 0 0 4px {Rgba(accent, 0.08)}, 0 10px 24px {Rgba(accent, 0.16)}"),
			"bloom" => ($"{Blooms(accent, turned: false)} right 2px top 2px / 40px 40px no-repeat, {Blooms(accent, turned: true)} right 2px bottom 2px / 40px 40px no-repeat",
				"solid", "1px", $"0 6px 20px {Rgba(text, 0.07)}"),
			"stage" => ($"{Bulbs(accent)} left 6px top 4px / 30px 10px repeat-x, {Bulbs(accent)} left 21px bottom 4px / 30px 10px repeat-x, "
				+ $"linear-gradient(0deg, {Rgba(accent, 0.16)}, {Rgba(accent, 0)}) left bottom / 100% 40% no-repeat",
				"solid", "1px", $"0 0 0 1px {Rgba(accent, 0.35)}, 0 0 20px {Rgba(accent, 0.28)}, 0 0 44px {Rgba(IdolCyan, 0.12)}"),
			"washi" => ($"{Washi(top: true)} center top / 96px 24px no-repeat, {Washi(top: false)} right bottom / 54px 54px no-repeat",
				"solid", "1px", dark ? "0 6px 16px rgba(0,0,0,0.4)" : "0 1px 0 rgba(0,0,0,0.04), 0 6px 14px rgba(90,70,30,0.10)"),
			"panel" => ($"{Tone(text, 0.55, 140, 90, "<radialGradient id='g' cx='1' cy='1' r='1'><stop offset='0.1' stop-color='white'/><stop offset='1' stop-color='black'/></radialGradient>")} right bottom / 140px 90px no-repeat",
				"solid", "3px", "none"),
			"jersey" => ($"linear-gradient({accent.Hex}, {accent.Hex}) left bottom / 100% 4px no-repeat, "
				+ $"linear-gradient({Rgba(text, 0.85)}, {Rgba(text, 0.85)}) left 0 bottom 6px / 100% 2px no-repeat",
				"solid", "1px", "0 10px 24px rgba(0,0,0,0.45)"),
			"chamfer" => (Chamfer(border, accent, text), "solid", "0px", "none"),
			"status" => ($"linear-gradient(180deg, {Rgba(border, 0.4)}, {Rgba(border, 0)}) left top / 100% 44px no-repeat, "
				+ Corners4($"<path d='M6 0L12 6L6 12L0 6Z' fill='{accent.Hex}'/>", 12, 3),
				"solid", "1px", $"0 0 0 1px {Rgba(border, 0.35)}, 0 0 18px {Rgba(border, 0.4)}"),
			"shoji" => ($"{HankoSeal(accent)} right 10px bottom 10px / 18px 18px no-repeat, {Corners4(KumikoCorner(text), 30, 4, shown: 26)}", "solid", "1px",
				$"inset 0 0 0 3px var(--surface), inset 0 0 0 4px {Rgba(text, 0.14)}, 0 0 26px {Rgba(accent, 0.07)}"),
			"inked" => ($"{CaptionBox(text)} left top / 186px 34px no-repeat, {BenDayFade(accent)} right 0 bottom 0 / 96px 64px no-repeat", "solid", "3px", $"6px 6px 0 {text.Hex}"),
			"titlecard" => ($"radial-gradient(ellipse at 50% 45%, {Rgba(text, 0)} 55%, {Rgba(text, 0.13)} 100%)", "solid", "3px",
				$"inset 0 0 0 4px var(--surface), inset 0 0 0 6px {text.Hex}, 0 3px 0 {Rgba(text, 0.25)}"),
			"chunky" => ($"linear-gradient(180deg, rgba(255,255,255,0.09), rgba(255,255,255,0) 38%)", "solid", "3px",
				$"inset 0 3px 0 rgba(255,255,255,0.10), 6px 7px 0 {ToyMagenta.Hex}"),
			"glitch" => (GlitchFrame(accent, border), "none", "0px",
				$"-3px 0 0 -1px {Rgba(NeonCyan, 0.3)}, 3px 0 0 -1px {Rgba(accent, 0.3)}, 0 0 26px {Rgba(accent, 0.10)}"),
			"neon" => ($"linear-gradient(180deg, {Rgba(accent, 0.10)}, {Rgba(accent, 0)} 38%), linear-gradient(0deg, {Rgba(SunOrange, 0.08)}, {Rgba(SunOrange, 0)} 30%)",
				"solid", "1px", $"0 0 0 1px {accent.Hex}, 0 0 7px 1px {Rgba(accent, 0.75)}, 0 0 24px {Rgba(accent, 0.35)}, inset 0 0 14px {Rgba(accent, 0.28)}"),
			"hull" => (HullPlate(accent), "solid", "1px",
				"inset 0 1px 0 rgba(255,255,255,0.10), inset 0 -2px 0 rgba(0,0,0,0.45), 0 8px 20px rgba(0,0,0,0.55)"),
			"elbow" => (ElbowFrame(accent), "none", "0px", "none"),
			"plating" => (RealRobotPlating(accent, border, text), "solid", "0px", "none"),
			"chrome-armor" => (SuperRobotArmor(accent), "outset", "3px",
				$"inset 0 2px 0 rgba(255,255,255,0.22), inset 0 -3px 0 rgba(0,0,0,0.5), 0 5px 0 #050102, 0 14px 30px rgba(0,0,0,0.7), 0 0 22px {Rgba(SuperRobotCrimson, 0.18)}"),
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
			"slant" => ("uppercase", "0.01em", "italic"),
			"wide" => ("uppercase", "0.14em", "normal"),
			_ => ("none", "normal", "normal"),
		};

		(css["title-color"], css["title-shadow"]) = Pick(Effect) switch
		{
			"glow" => ("var(--text)", $"0 0 2px {Rgba(accent, 0.45)}, 0 0 12px {Rgba(accent, 0.55)}"),
			"gilt" => (accent.Hex, dark ? $"0 1px 0 rgba(0,0,0,0.5), 0 0 10px {Rgba(accent, 0.22)}" : "0 1px 0 rgba(255,255,255,0.7)"),
			"emboss" => ("var(--text)", dark ? "0 1px 0 rgba(255,255,255,0.08), 0 -1px 0 rgba(0,0,0,0.6)" : "0 1px 0 rgba(255,255,255,0.9), 0 -1px 0 rgba(0,0,0,0.12)"),
			"ember" => ("var(--text)", $"0 1px 0 rgba(0,0,0,0.7), 0 0 10px {Rgba(accent, 0.5)}"),
			"bleed" => ("var(--text)", $"0 0 1px {Rgba(text, 0.75)}, 0.6px 0.6px 0 {Rgba(text, 0.3)}"),
			"shine" => (accent.Hex, $"0 1px 0 rgba(255,255,255,0.9), 0 2px 0 {Rgba(AnimeLavender, 0.45)}, 0 0 14px {Rgba(accent, 0.30)}"),
			"dreamy" => ("var(--text)", $"0 0 6px rgba(255,255,255,0.95), 0 0 2px rgba(255,255,255,0.95), 0 3px 12px {Rgba(accent, 0.28)}"),
			"lightstick" => ("var(--text)", $"0 0 3px {Rgba(accent, 0.9)}, 0 0 12px {Rgba(accent, 0.7)}, 2px 2px 0 {Rgba(IdolCyan, 0.55)}, 0 0 28px {Rgba(IdolCyan, 0.25)}"),
			"pencil" => ("var(--text)", $"1.5px 1.5px 0 {Rgba(accent, 0.22)}"),
			"inked" => ("var(--text)", $"2px 2px 0 {Rgba(accent, 0.9)}"),
			"varsity" => ("var(--text)", $"-1px -1px 0 {accent.Hex}, 1px -1px 0 {accent.Hex}, -1px 1px 0 {accent.Hex}, 1px 1px 0 {accent.Hex}, 3px 3px 0 rgba(0,0,0,0.55)"),
			"stencil" => ("var(--warn)", "0 2px 0 rgba(0,0,0,0.55)"),
			"pixel" => (accent.Hex, $"2px 2px 0 rgba(0,0,0,0.65), 0 0 12px {Rgba(accent, 0.35)}"),
			"lantern" => ("var(--text)", $"0 1px 0 rgba(0,0,0,0.6), 0 0 14px {Rgba(accent, 0.45)}, 0 0 34px {Rgba(accent, 0.22)}"),
			"inkpop" => (accent.Hex, $"{InkOutline(text, 0.035)}, 0.07em 0.07em 0 {text.Hex}"),
			"cartoon" => ("var(--text)", $"0.08em 0.08em 0 {Rgba(accent, 0.6)}"),
			"toybox" => (accent.Hex, $"{InkOutline(ThemeColor.Black, 0.065)}, 0.15em 0.15em 0 {ToyMagenta.Hex}"),
			"rgbsplit" => ("var(--text)", $"-2px 0 0 {Rgba(NeonCyan, 0.8)}, 2px 0 0 {Rgba(accent, 0.8)}, 0 0 16px {Rgba(accent, 0.35)}"),
			"retro" => ("var(--text)", $"0 0 2px {Rgba(accent, 0.9)}, 0 2px 0 {accent.Hex}, 0 3px 0 {Rgba(SunOrange, 0.7)}, 0 0 14px {Rgba(accent, 0.7)}, 0 0 30px {Rgba(SunOrange, 0.35)}"),
			"console" => (accent.Hex, "none"),
			"decal" => (accent.Hex, $"0.05em 0.05em 0 {Rgba(RealRobotRed, 0.85)}"),
			"blazing" => (accent.Hex, $"2px 2px 0 {SuperRobotEmber.Hex}, 3px 3px 0 {SuperRobotEmber.Hex}, 4px 4px 0 #000, 0 0 10px {Rgba(SuperRobotOrange, 0.7)}, 0 0 26px {Rgba(SuperRobotCrimson, 0.6)}"),
			_ => ("var(--text)", "none"),
		};

		css["image-filter"] = Pick(Imagery) switch
		{
			"tint" => $"grayscale(1) sepia(1) hue-rotate({F(Hue(accent) - 38)}deg) saturate(2.4) brightness({(dark ? "0.85" : "1")})",
			"sepia" => "sepia(0.85) contrast(1.05) brightness(0.95)",
			"mono" => "grayscale(1) contrast(1.1)",
			"noir" => "grayscale(1) contrast(1.5) brightness(0.85)",
			"pastel" => $"grayscale(1) sepia(0.7) hue-rotate({F(Hue(accent) - 38)}deg) saturate(1.8) brightness(1.18) contrast(0.85)",
			"airy" => "saturate(0.8) contrast(0.9) brightness(1.12)",
			"spotlit" => "saturate(1.6) contrast(1.18) brightness(1.02)",
			"sunny" => "sepia(0.22) saturate(1.3) brightness(1.06)",
			"ink" => "grayscale(1) contrast(1.6) brightness(1.08)",
			"floodlit" => "saturate(1.25) contrast(1.15) brightness(1.05)",
			"steel" => "grayscale(0.65) sepia(0.15) contrast(1.2) brightness(0.92)",
			"vivid" => "saturate(1.45) contrast(1.05) hue-rotate(-8deg)",
			"indigo" => "grayscale(1) sepia(1) hue-rotate(190deg) saturate(1.7) brightness(0.8)",
			"print" => "sepia(0.2) saturate(1.9) contrast(1.2)",
			"film" => "grayscale(1) sepia(0.35) contrast(1.35) brightness(1.05)",
			"cel" => "saturate(2.1) contrast(1.15) hue-rotate(-14deg) brightness(1.08)",
			"neon" => "saturate(1.8) contrast(1.15) brightness(0.9) hue-rotate(-25deg)",
			"dusk" => "grayscale(1) sepia(1) hue-rotate(255deg) saturate(2.6) contrast(1.05)",
			"warm" => "sepia(0.25) saturate(1.3) contrast(1.1) brightness(0.95)",
			"hangar" => "saturate(1.15) contrast(1.12) brightness(1.05) hue-rotate(-6deg)",
			"hotblooded" => "sepia(0.18) saturate(1.5) contrast(1.18) hue-rotate(-8deg)",
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
		// Compared as the byte channels, so no floating-point equality is involved.
		int r = c.R, g = c.G, b = c.B;
		int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
		if (d == 0) return 0;
		var h = max == r ? (double)(g - b) / d % 6 : max == g ? (double)(b - r) / d + 2 : (double)(r - g) / d + 4;
		return Math.Round((h * 60 + 360) % 360);
	}

	/// <summary>
	/// A corner drawing (drawn for the top-left) placed on the corners of a card, turned for each, or with
	/// <paramref name="mirror"/> flipped instead. <paramref name="topOnly"/> keeps it to the two top corners, and
	/// <paramref name="shown"/> draws it smaller than it was drawn, to keep it clear of a card's text.
	/// </summary>
	private static string Corners4(string drawing, int size, int inset, bool mirror = false, bool topOnly = false, int? shown = null)
	{
		var px = shown ?? size;
		var c = F(size / 2.0);
		string[] turns = mirror
			? ["", $"translate({size} 0) scale(-1 1)", $"translate({size} {size}) scale(-1 -1)", $"translate(0 {size}) scale(1 -1)"]
			: ["", $"rotate(90 {c} {c})", $"rotate(180 {c} {c})", $"rotate(270 {c} {c})"];
		string[] places = [$"left {inset}px top {inset}px", $"right {inset}px top {inset}px", $"right {inset}px bottom {inset}px", $"left {inset}px bottom {inset}px"];
		return string.Join(", ", turns.Zip(places, (turn, place) =>
			$"{Svg($"<g transform='{turn}'>{drawing}</g>", px, px, $"0 0 {size} {size}")} {place} / {px}px {px}px no-repeat").Take(topOnly ? 2 : 4));
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
			"parchment" => ($"radial-gradient(ellipse 75% 70% at 50% 45%, {Rgba(ink, 0)} 50%, {Rgba(accent, dark ? 0.12 : 0.09)} 85%, {Rgba(ink, dark ? 0.2 : 0.15)} 100%), "
				+ $"{Noise(ink, dark ? 0.08 : 0.10, 0.018, 500)}, {Noise(ink, dark ? 0.07 : 0.10)}", "100% 100%, 100% 100%, 180px 180px"),
			// Foxing: the brown spots of an old book, on a faint weave, browning at the edges.
			"foxing" => ($"radial-gradient(ellipse at 50% 45%, {Rgba(ink, 0)} 55%, {Rgba(ink, dark ? 0.14 : 0.09)} 100%), "
				+ $"radial-gradient(circle at 40px 70px, {Rgba(accent, 0.14)} 0 2px, {Rgba(accent, 0)} 9px), "
				+ $"radial-gradient(circle at 210px 30px, {Rgba(accent, 0.10)} 0 3px, {Rgba(accent, 0)} 12px), "
				+ $"radial-gradient(circle at 270px 240px, {Rgba(accent, 0.12)} 0 1.5px, {Rgba(accent, 0)} 7px), "
				+ $"radial-gradient(circle at 120px 280px, {Rgba(accent, 0.09)} 0 4px, {Rgba(accent, 0)} 14px), "
				+ $"{Noise(ink, dark ? 0.06 : 0.09)}, "
				+ $"repeating-linear-gradient(0deg, {Rgba(ink, faint)} 0 1px, {Rgba(ink, 0)} 1px 3px)",
				"100% 100%, 317px 289px, 411px 373px, 523px 467px, 389px 541px, 180px 180px, auto"),
			// Blood: a red pooling from below and the corner, stained and grainy.
			"blood" => ($"radial-gradient(120% 70% at 50% 115%, {Rgba(accent, 0.24)}, {Rgba(accent, 0)} 60%), "
				+ $"radial-gradient(60% 50% at 100% 100%, {Rgba(accent, 0.08)}, {Rgba(accent, 0)} 70%), "
				+ $"{Noise(accent, 0.10, 0.02, 400)}, {Noise(ink, 0.06)}", "100% 100%, 100% 100%, 100% 100%, 180px 180px"),
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
			// Transformation sparkles: four-point stars and glitter twinkling over a candy-pink and lavender wash.
			"sparkles" => ($"radial-gradient(55% 45% at 10% 0%, {Rgba(accent, 0.14)}, {Rgba(accent, 0)} 70%), "
				+ $"radial-gradient(55% 55% at 100% 100%, {Rgba(AnimeLavender, 0.18)}, {Rgba(AnimeLavender, 0)} 70%), {SparkleField(accent)}",
				"100% 100%, 100% 100%, 300px 300px"),
			// Shojo bubbles: a screentone of fine dots behind floating bubbles and big outlined blossoms.
			"bubbles" => ($"radial-gradient(70% 50% at 85% 0%, {Rgba(ShojoCoral, 0.12)}, {Rgba(ShojoCoral, 0)} 70%), {BubbleField(ink, accent)}, "
				+ $"radial-gradient(circle, {Rgba(ink, 0.07)} 0 1px, {Rgba(ink, 0)} 1.4px)", "100% 100%, 460px 460px, 7px 7px"),
			// A concert stage: coloured spotlight beams from the rig above, a glow off the floor, glitter in the air.
			"spotlights" => ($"{Beams(accent)}, radial-gradient(90% 40% at 50% 110%, {Rgba(accent, 0.22)}, {Rgba(accent, 0)} 70%), "
				+ $"radial-gradient(1.5px 1.5px at 30px 40px, {Rgba(accent, 0.8)}, {Rgba(accent, 0)}), "
				+ $"radial-gradient(1.5px 1.5px at 120px 90px, {Rgba(IdolCyan, 0.7)}, {Rgba(IdolCyan, 0)}), "
				+ $"radial-gradient(1.5px 1.5px at 75px 150px, {Rgba(IdolLemon, 0.7)}, {Rgba(IdolLemon, 0)})",
				"100% 100%, 100% 100%, 170px 170px, 170px 170px, 170px 170px"),
			// School notebook paper: blue ruled lines, a red double margin line, and a little grain.
			"notebook" => ($"linear-gradient(90deg, rgba(0,0,0,0) 0 {NotebookMargin}px, rgba(222,96,108,0.45) {NotebookMargin}px {NotebookMargin + 1}px, "
				+ $"rgba(0,0,0,0) {NotebookMargin + 1}px {NotebookMargin + 4}px, rgba(222,96,108,0.45) {NotebookMargin + 4}px {NotebookMargin + 5}px, rgba(0,0,0,0) {NotebookMargin + 5}px), "
				+ $"repeating-linear-gradient(180deg, rgba(0,0,0,0) 0 31px, rgba(96,150,210,{(dark ? "0.18" : "0.30")}) 31px 32px), {Noise(ink, dark ? 0.05 : 0.06)}",
				"100% 100%, auto, 180px 180px"),
			// Manga focus lines converging on the page, over screentone rising from the foot.
			"speedlines" => ($"{SpeedLines(ink, dark)}, {Tone(ink, dark ? 0.18 : 0.22, 1280, 800, "<linearGradient id='g' x1='0' y1='0' x2='0' y2='1'><stop offset='0.4' stop-color='black'/><stop offset='1' stop-color='white'/></linearGradient>")}",
				"100% 100%, 100% 100%"),
			// A court's markings at night, under two floodlights.
			"court" => ($"radial-gradient(45% 60% at 6% -8%, {Rgba(ink, 0.2)}, {Rgba(ink, 0)} 70%), "
				+ $"radial-gradient(45% 60% at 94% -8%, {Rgba(ink, 0.2)}, {Rgba(ink, 0)} 70%), "
				+ $"{Court(ink, accent)}, {Noise(ink, 0.04)}", "100% 100%, 100% 100%, 100% 100%, 180px 180px"),
			// Riveted armour plates under a work light, with a band of hazard stripes along the top.
			"hazard" => ($"{HazardBand()}, radial-gradient(70% 55% at 50% 0%, {Rgba(ink, 0.06)}, {Rgba(ink, 0)} 70%), {Plates(ink)}, {Noise(ink, 0.04)}",
				"2400px 1600px, 100% 100%, 240px 160px, 180px 180px"),
			// A magic circle glowing behind the page, with motes of light.
			"sigil" => ($"radial-gradient(40% 55% at 58% 55%, {Rgba(accent, 0.07)}, {Rgba(accent, 0)} 70%), {Sigil(ink, accent)}, "
				+ $"radial-gradient(1.5px 1.5px at 37px 51px, {Rgba(accent, 0.55)}, {Rgba(accent, 0)}), "
				+ $"radial-gradient(1px 1px at 141px 113px, {Rgba(ink, 0.45)}, {Rgba(ink, 0)})",
				"100% 100%, 100% 100%, 173px 173px, 173px 173px"),
			// Sumi: a moon behind bands of mist, lantern light from below, waves along the foot, ink-wash cloud and washi fibres.
			"sumi" => ($"{YokaiSky(ink)}, {Seigaiha(ink)}, "
				+ $"radial-gradient(55% 45% at 0% 100%, {Rgba(accent, 0.2)}, {Rgba(accent, 0)} 70%), "
				+ $"{Noise(ink, 0.06, 0.006, 800)}, {WashiFibres(ink)}",
				"100% 100%, 100% 100%, 100% 100%, 800px 800px, 260px 260px"),
			// Ben-Day dots: red and cyan halftone screens on yellowing newsprint.
			"benday" => ($"radial-gradient(ellipse at 50% 45%, {Rgba(ink, 0)} 60%, {Rgba(ink, 0.08)} 100%), "
				+ $"radial-gradient(circle at 3px 3px, {Rgba(accent, 0.2)} 0 1.9px, {Rgba(accent, 0)} 2.5px), "
				+ $"radial-gradient(circle at 8.5px 8.5px, rgba(0,150,214,0.2) 0 1.9px, rgba(0,150,214,0) 2.5px), "
				+ $"{Noise(ink, 0.06)}", "100% 100%, 11px 11px, 11px 11px, 180px 180px"),
			// Film grain: an old print's vignette, scratches and dust, a flicker, and grain.
			"filmgrain" => ($"radial-gradient(ellipse 80% 75% at 50% 45%, {Rgba(ink, 0)} 45%, {Rgba(ink, 0.42)} 100%), "
				+ $"{FilmScratches(ink)}, {Noise(ink, 0.09, 0.012, 600)}, {Noise(ink, 0.17, 0.95)}",
				"100% 100%, 700px 820px, 600px 600px, 160px 160px"),
			// Star bursts: a toy-box night sky of sparkles, bursts and lightning, lit magenta and cyan from the corners.
			"starburst" => ($"radial-gradient(60% 50% at 0% 0%, {Rgba(ToyMagenta, 0.22)}, {Rgba(ToyMagenta, 0)} 70%), "
				+ $"radial-gradient(55% 50% at 100% 100%, {Rgba(ToyCyan, 0.16)}, {Rgba(ToyCyan, 0)} 70%), {ToyStarbursts(ink, accent)}",
				"100% 100%, 100% 100%, 520px 520px"),
			// Neon city rain: streaks falling past a magenta glow from the street and a cyan one from a sign above.
			"rain" => ($"radial-gradient(45% 40% at 40% 104%, {Rgba(accent, 0.20)}, {Rgba(accent, 0)} 70%), "
				+ $"radial-gradient(35% 30% at 96% 4%, {Rgba(NeonCyan, 0.14)}, {Rgba(NeonCyan, 0)} 70%), "
				+ $"radial-gradient(18% 10% at 86% 78%, {Rgba(AcidYellow, 0.10)}, {Rgba(AcidYellow, 0)} 70%), "
				+ $"{RainStreaks(ink, accent)}", "100% 100%, 100% 100%, 100% 100%, 260px 260px"),
			// An outrun sunset: a striped sun sinking behind a neon grid floor that runs to the horizon.
			"outrun" => (Outrun(accent), "100% 100%"),
			// Deep space: three depths of crisp stars, a faint galactic band, and a planet's lit rim rising from the corner.
			"galaxy" => ($"radial-gradient(circle at 112% 128%, rgb(5,7,13) 0 34%, rgba(140,180,255,0.55) 34.4%, rgba(110,150,240,0.16) 36%, rgba(110,150,240,0) 42%), "
				+ $"linear-gradient(118deg, rgba(0,0,0,0) 32%, rgba(170,160,230,0.05) 44%, rgba(255,226,190,0.08) 50%, rgba(170,160,230,0.05) 56%, rgba(0,0,0,0) 68%), "
				+ $"{Starfield(ink, 200, 70, 5, 0.3)}, {Starfield(ink, 320, 60, 11, 0.8)}, {Starfield(ink, 530, 14, 29, 1.4)}",
				"100% 100%, 100% 100%, 200px 200px, 320px 320px, 530px 530px"),
			// A console's quiet readout: coloured segments stacked down the page's right edge.
			"readout" => (Readout(accent), "100% 100%"),
			// Armour panels: a mobile suit's pale plating, engraved panel lines with a lit edge, hatches, and small maintenance decals.
			"armor-panels" => ($"linear-gradient(170deg, rgba(255,255,255,{(dark ? "0.03" : "0.55")}), rgba(255,255,255,0) 45%), {RealRobotPanels(ink, accent, dark)}",
				"100% 100%, 560px 420px"),
			// A super robot's finishing-move backdrop: red, orange and gold rays bursting from below the page, a crimson blaze, and drifting embers.
			"sunburst-rays" => ($"{SuperRobotRays()}, radial-gradient(110% 70% at 50% 108%, {Rgba(SuperRobotCrimson, 0.38)}, {Rgba(SuperRobotCrimson, 0)} 70%), {SuperRobotEmbers()}",
				"100% 100%, 100% 100%, 340px 340px"),
			_ => ("none", "auto"),
		};
	}

	/// <summary>The second colour of the anime themes' drawings, beside the theme's accent.</summary>
	private static readonly ThemeColor AnimeLavender = new(150, 110, 220);

	private static readonly ThemeColor ShojoCoral = new(238, 122, 112);

	private static readonly ThemeColor IdolCyan = new(54, 226, 255);

	private static readonly ThemeColor IdolLemon = new(255, 222, 74);

	/// <summary>Where the notebook texture's margin line falls from the window's left edge: just past the rail and page sidebar (72px + 232px).</summary>
	private const int NotebookMargin = 316;

	/// <summary>A four-point sparkle: four points drawn in toward the centre.</summary>
	private static string Sparkle(double x, double y, double r, ThemeColor color, double alpha) =>
		$"<path fill='{color.Hex}' fill-opacity='{F(alpha)}' d='M{F(x)} {F(y - r)}Q{F(x)} {F(y)} {F(x + r)} {F(y)}Q{F(x)} {F(y)} {F(x)} {F(y + r)}"
		+ $"Q{F(x)} {F(y)} {F(x - r)} {F(y)}Q{F(x)} {F(y)} {F(x)} {F(y - r)}Z'/>";

	/// <summary>Sparkles of several sizes and glitter dots over one tile, in the accent and lavender.</summary>
	private static string SparkleField(ThemeColor accent)
	{
		(int X, int Y, int R, bool Lavender, double Alpha)[] sparkles =
			[(40, 50, 14, false, 0.30), (190, 30, 8, true, 0.40), (250, 150, 18, true, 0.26), (120, 170, 7, false, 0.38), (70, 260, 11, true, 0.30), (220, 255, 6, false, 0.40), (150, 95, 4, true, 0.5)];
		(int X, int Y)[] glitter = [(100, 40), (280, 60), (20, 140), (180, 210), (290, 280), (130, 280), (60, 110)];
		var body = string.Concat(sparkles.Select(s => Sparkle(s.X, s.Y, s.R, s.Lavender ? AnimeLavender : accent, s.Alpha)))
			+ string.Concat(glitter.Select((g, i) => $"<circle cx='{g.X}' cy='{g.Y}' r='{(i % 2 == 0 ? "1.6" : "1.1")}' fill='{(i % 2 == 0 ? accent.Hex : AnimeLavender.Hex)}' fill-opacity='0.45'/>"));
		return Svg(body, 300, 300);
	}

	/// <summary>A five-petalled blossom in outline, as a manga page draws one.</summary>
	private static string Blossom(double x, double y, double r, ThemeColor color, double alpha, int turn = 0)
	{
		var petals = string.Concat(Enumerable.Range(0, 5).Select(i =>
			$"<ellipse cx='0' cy='{F(-r * 0.55)}' rx='{F(r * 0.36)}' ry='{F(r * 0.55)}' transform='rotate({i * 72})'/>"));
		return Flower(x, y, turn, color, alpha, $"{petals}<circle r='{F(r * 0.18)}' fill-opacity='{F(alpha)}'/>");
	}

	/// <summary>A cherry blossom: five petals, each notched at the tip.</summary>
	private static string Sakura(double x, double y, double r, ThemeColor color, double alpha, int turn = 0)
	{
		var petal = $"<path d='M0 0C{F(-r * 0.5)} {F(-r * 0.3)} {F(-r * 0.45)} {F(-r * 0.95)} {F(-r * 0.14)} {F(-r)}L0 {F(-r * 0.8)}"
			+ $"L{F(r * 0.14)} {F(-r)}C{F(r * 0.45)} {F(-r * 0.95)} {F(r * 0.5)} {F(-r * 0.3)} 0 0Z' transform='rotate({{0}})'/>";
		var petals = string.Concat(Enumerable.Range(0, 5).Select(i => petal.Replace("{0}", (i * 72).ToString(CultureInfo.InvariantCulture))));
		return Flower(x, y, turn, color, alpha, $"{petals}<circle r='{F(r * 0.12)}' fill-opacity='{F(alpha)}'/>");
	}

	/// <summary>A daisy: a ring of narrow petals round a solid heart.</summary>
	private static string Daisy(double x, double y, double r, ThemeColor color, double alpha, int turn = 0)
	{
		var petals = string.Concat(Enumerable.Range(0, 12).Select(i =>
			$"<ellipse cx='0' cy='{F(-r * 0.58)}' rx='{F(r * 0.12)}' ry='{F(r * 0.4)}' transform='rotate({i * 30})'/>"));
		return Flower(x, y, turn, color, alpha, $"{petals}<circle r='{F(r * 0.22)}' fill-opacity='{F(alpha)}'/>");
	}

	/// <summary>A floret of four round petals, as in a hydrangea head.</summary>
	private static string Floret(double x, double y, double r, ThemeColor color, double alpha, int turn = 0)
	{
		var petals = string.Concat(Enumerable.Range(0, 4).Select(i =>
		{
			var (px, py) = Polar(r * 0.42, i * 90.0);
			return $"<circle cx='{F(px)}' cy='{F(py)}' r='{F(r * 0.36)}'/>";
		}));
		return Flower(x, y, turn, color, alpha, $"{petals}<circle r='{F(r * 0.1)}' fill-opacity='{F(alpha)}'/>");
	}

	/// <summary>Places a flower's petals: tinted fill, a finer outline in the same colour.</summary>
	private static string Flower(double x, double y, int turn, ThemeColor color, double alpha, string petals) =>
		$"<g transform='translate({F(x)} {F(y)}) rotate({turn})' fill='{color.Hex}' fill-opacity='{F(alpha * 0.45)}' stroke='{color.Hex}' stroke-opacity='{F(alpha)}' stroke-width='1.1'>{petals}</g>";

	/// <summary>Shojo background bubbles, white with a fine rim and a highlight, among large blossoms.</summary>
	private static string BubbleField(ThemeColor ink, ThemeColor accent)
	{
		(int X, int Y, int R)[] bubbles = [(70, 80, 46), (300, 60, 22), (390, 210, 58), (180, 250, 16), (90, 380, 30), (300, 400, 12), (230, 150, 9)];
		var body = string.Concat(bubbles.Select(b =>
			$"<circle cx='{b.X}' cy='{b.Y}' r='{b.R}' fill='rgb(255,255,255)' fill-opacity='0.55' stroke='{ink.Hex}' stroke-opacity='0.10' stroke-width='1'/>"
			+ $"<path d='M{F(b.X - b.R * 0.62)} {F(b.Y - b.R * 0.2)}A{F(b.R * 0.66)} {F(b.R * 0.66)} 0 0 1 {F(b.X - b.R * 0.15)} {F(b.Y - b.R * 0.64)}' fill='none' stroke='rgb(255,255,255)' stroke-width='{F(Math.Max(1.5, b.R / 10.0))}' stroke-linecap='round'/>"));
		// Small flowers of four kinds and three colours, so no two near each other match.
		body += Sakura(200, 62, 15, ShojoCoral, 0.26, 10) + Daisy(36, 226, 12, accent, 0.2) + Floret(410, 350, 11, AnimeLavender, 0.3, 20)
			+ Blossom(238, 336, 9, ShojoCoral, 0.24, 30) + Sakura(150, 420, 8, accent, 0.22, 50) + Daisy(340, 140, 8, ShojoCoral, 0.2, 15)
			+ Floret(120, 160, 7, ShojoCoral, 0.24) + Sakura(440, 40, 10, AnimeLavender, 0.24, 64) + Blossom(20, 420, 11, accent, 0.18, 5)
			+ Floret(290, 260, 6, accent, 0.22, 45);
		return Svg(body, 460, 460);
	}

	/// <summary>Spotlight beams spreading down from the rig: pink, cyan and yellow, each fading toward the floor.</summary>
	private static string Beams(ThemeColor accent)
	{
		(string Id, ThemeColor Color)[] lights = [("p", accent), ("c", IdolCyan), ("y", IdolLemon)];
		var defs = string.Concat(lights.Select(l =>
			$"<linearGradient id='{l.Id}' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='{l.Color.Hex}' stop-opacity='0.30'/>"
			+ $"<stop offset='0.75' stop-color='{l.Color.Hex}' stop-opacity='0.04'/><stop offset='1' stop-color='{l.Color.Hex}' stop-opacity='0'/></linearGradient>"));
		(string Id, string Points, int Cx)[] beams =
			[("p", "180,0 210,0 560,800 200,800", 195), ("c", "520,0 545,0 800,800 400,800", 532), ("y", "860,0 885,0 980,800 620,800", 872), ("p", "1150,0 1175,0 1180,800 820,800", 1162)];
		var body = string.Concat(beams.Select(b => $"<polygon points='{b.Points}' fill='url(#{b.Id})'/>"
			+ $"<ellipse cx='{b.Cx}' cy='0' rx='34' ry='14' fill='url(#{b.Id})'/>"));
		return Svg($"<defs>{defs}</defs>{body}", 1200, 800, "0 0 1200 800");
	}

	/// <summary>A ribbon tied in a bow, with a sparkle on one loop.</summary>
	private static string Bow(ThemeColor accent) => Svg(
		$"<g fill='{accent.Hex}' stroke='rgb(255,255,255)' stroke-opacity='0.6' stroke-width='0.8'>"
		+ "<path d='M31 15L22 29L26.5 27L28.5 30L33 16Z'/><path d='M35 15L44 29L39.5 27L37.5 30L33 16Z'/>"
		+ "<path d='M33 13C24 2 8 0 6 9C4 18 20 19 33 13Z'/><path d='M33 13C42 2 58 0 60 9C62 18 46 19 33 13Z'/>"
		+ "<ellipse cx='33' cy='13' rx='4.5' ry='5'/></g>"
		+ $"<path d='M12 9C16 6 22 7 26 11' fill='none' stroke='rgb(255,255,255)' stroke-opacity='0.7' stroke-width='1.4' stroke-linecap='round'/>"
		+ Sparkle(54, 5, 4, AnimeLavender, 0.9), 66, 30, "0 0 66 30");

	/// <summary>A spray of blossoms and leaves for a card's corner; turned, for the corner below.</summary>
	private static string Blooms(ThemeColor accent, bool turned) => Svg(
		$"<g transform='{(turned ? "rotate(90 29 29)" : "")}'>"
		+ $"<path d='M50 40C40 40 34 34 32 26C40 26 46 30 50 40Z' fill='{accent.Hex}' fill-opacity='0.45'/>"
		+ $"<path d='M12 10C14 20 20 24 28 24C26 16 20 10 12 10Z' fill='{accent.Hex}' fill-opacity='0.40'/>"
		+ (turned
			? Floret(34, 22, 13, AnimeLavender, 0.75, 20) + Daisy(16, 38, 10, accent, 0.7) + Blossom(47, 45, 7, ShojoCoral, 0.7)
			: Sakura(36, 20, 15, ShojoCoral, 0.8, 12) + Blossom(17, 36, 9, accent, 0.65, 40) + Floret(47, 45, 6, ShojoCoral, 0.7)) + "</g>",
		58, 58, "0 0 58 58");

	/// <summary>A row of stage lights: pink, cyan and yellow bulbs with their glow.</summary>
	private static string Bulbs(ThemeColor accent) => Svg(
		string.Concat(new[] { (5, accent), (15, IdolCyan), (25, IdolLemon) }.Select(b =>
			$"<circle cx='{b.Item1}' cy='5' r='4.5' fill='{b.Item2.Hex}' fill-opacity='0.28'/><circle cx='{b.Item1}' cy='5' r='2' fill='{b.Item2.Hex}'/>")),
		30, 10);

	/// <summary>Washi tape: a mint strip with white dots across the top edge, or a candy-striped one across the corner below.</summary>
	private static string Washi(bool top)
	{
		const string ends = "L2 18L0 16L2 14L0 12L2 10L0 8L2 6";
		if (top)
		{
			return Svg("<defs><pattern id='w' width='9' height='9' patternUnits='userSpaceOnUse'><rect width='9' height='9' fill='rgb(128,204,178)'/>"
				+ "<circle cx='4.5' cy='4.5' r='1.7' fill='rgb(255,255,255)'/></pattern></defs>"
				+ $"<path d='M0 4H96L94 6L96 8L94 10L96 12L94 14L96 16L94 18L96 20H0{ends}Z' fill='url(#w)' fill-opacity='0.88' transform='rotate(-3 48 12)'/>", 96, 24);
		}
		return Svg("<defs><pattern id='w' width='8' height='8' patternUnits='userSpaceOnUse' patternTransform='rotate(45)'><rect width='8' height='8' fill='rgb(255,214,120)'/>"
			+ "<rect width='4' height='8' fill='rgb(240,128,140)'/></pattern></defs>"
			+ "<path d='M-22 10H52L50 12L52 14L50 16L52 18L50 20L52 22L50 24H-22Z' fill='url(#w)' fill-opacity='0.85' transform='rotate(180 27 27) rotate(-42 15 17)'/>", 54, 54);
	}
	/// <summary>A point on a circle, from its radius and an angle in degrees.</summary>
	private static (double X, double Y) Polar(double radius, double degrees) =>
		(radius * Math.Cos(degrees * Math.PI / 180), radius * Math.Sin(degrees * Math.PI / 180));

	/// <summary>Screentone: a dot grid shaded in by <paramref name="gradient"/> (an SVG gradient with id 'g'; white shows the dots).</summary>
	private static string Tone(ThemeColor ink, double alpha, int width, int height, string gradient) => Svg(
		$"<defs><pattern id='d' width='6' height='6' patternUnits='userSpaceOnUse'><circle cx='3' cy='3' r='1.4' fill='{ink.Hex}' fill-opacity='{F(alpha)}'/></pattern>"
		+ $"{gradient}<mask id='m'><rect width='100%' height='100%' fill='url(#g)'/></mask></defs>"
		+ "<rect width='100%' height='100%' fill='url(#d)' mask='url(#m)'/>", width, height, $"0 0 {width} {height}", "xMidYMid slice");

	/// <summary>Manga focus lines: thin wedges of ink converging on the middle of the page, which they leave clear.</summary>
	private static string SpeedLines(ThemeColor ink, bool dark)
	{
		var lines = new System.Text.StringBuilder();
		var seed = 7u;
		double Next()
		{
			seed = seed * 1664525u + 1013904223u;
			return (seed >> 8) / 16777216.0;
		}
		for (var a = 0.0; a < 360; a += 2.6 + Next() * 3.6)
		{
			var spread = 0.2 + Next() * 0.6;
			var (x0, y0) = Polar(430 + Next() * 220, a);
			var (x1, y1) = Polar(1400, a - spread / 2);
			var (x2, y2) = Polar(1400, a + spread / 2);
			lines.Append($"<path d='M{F(x0)} {F(y0)}L{F(x1)} {F(y1)}L{F(x2)} {F(y2)}Z' fill-opacity='{F(0.45 + Next() * 0.55)}'/>");
		}
		var body = $"<g transform='translate(700 400)' fill='{ink.Hex}' opacity='{(dark ? "0.14" : "0.2")}'>{lines}</g>";
		return Svg(body, 1280, 800, "0 0 1280 800", "xMidYMid slice");
	}

	/// <summary>A court's markings: outline, halfway line and centre circle, the keys and the three-point arcs.</summary>
	private static string Court(ThemeColor ink, ThemeColor accent) => Svg(
		$"<circle cx='640' cy='400' r='95' fill='{accent.Hex}' fill-opacity='0.06'/>"
		+ $"<g fill='none' stroke='{ink.Hex}' stroke-opacity='0.10' stroke-width='3'>"
		+ "<rect x='60' y='70' width='1160' height='660'/><path d='M640 70V730'/><circle cx='640' cy='400' r='95'/><circle cx='640' cy='400' r='24'/>"
		+ "<path d='M60 300H290V500H60'/><circle cx='290' cy='400' r='70'/><path d='M1220 300H990V500H1220'/><circle cx='990' cy='400' r='70'/>"
		+ "<path d='M60 130H200A290 290 0 0 1 200 670H60'/><path d='M1220 130H1080A290 290 0 0 0 1080 670H1220'/></g>",
		1280, 800, "0 0 1280 800", "xMidYMid slice");

	/// <summary>Armour plates: a dark seam with a lit edge, and rivets at the corners and halfway along.</summary>
	private static string Plates(ThemeColor ink) => Svg(
		$"<path d='M0 0.5H240M0.5 0V160' stroke='black' stroke-opacity='0.5'/><path d='M0 1.5H240M1.5 0V160' stroke='{ink.Hex}' stroke-opacity='0.06'/>"
		+ $"<g fill='{ink.Hex}' fill-opacity='0.14'><circle cx='9' cy='9' r='2'/><circle cx='231' cy='9' r='2'/><circle cx='9' cy='151' r='2'/>"
		+ "<circle cx='231' cy='151' r='2'/><circle cx='120' cy='9' r='2'/><circle cx='120' cy='151' r='2'/></g>", 240, 160);

	/// <summary>Yellow and black hazard stripes along the top edge of the page.</summary>
	private static string HazardBand() => Svg(
		"<defs><pattern id='h' width='22' height='22' patternUnits='userSpaceOnUse' patternTransform='rotate(45)'><rect width='11' height='22' fill='rgb(240,184,32)'/></pattern></defs>"
		+ "<rect width='2400' height='9' fill='rgb(12,12,12)' fill-opacity='0.7'/><rect width='2400' height='9' fill='url(#h)' fill-opacity='0.6'/>", 2400, 1600);

	/// <summary>A magic circle: a band of runes between rings, a hexagram, an inner ring of spokes and seals at the star's points.</summary>
	private static string Sigil(ThemeColor ink, ThemeColor accent)
	{
		var g = new System.Text.StringBuilder("<circle r='350'/><circle r='318'/><circle r='306'/><circle r='160'/><circle r='146'/><circle r='40'/>");
		for (var i = 0; i < 72; i++)
		{
			var (x0, y0) = Polar(322, i * 5.0);
			var (x1, y1) = Polar(i % 3 == 0 ? 346 : 334, i * 5.0);
			g.Append($"<path d='M{F(x0)} {F(y0)}L{F(x1)} {F(y1)}'/>");
		}
		for (var t = 0; t < 2; t++)
		{
			var p = Enumerable.Range(0, 3).Select(k => Polar(306, t * 60.0 - 90 + k * 120.0)).ToList();
			g.Append($"<path d='M{F(p[0].X)} {F(p[0].Y)}L{F(p[1].X)} {F(p[1].Y)}L{F(p[2].X)} {F(p[2].Y)}Z'/>");
		}
		for (var i = 0; i < 12; i++)
		{
			var (x0, y0) = Polar(40, i * 30.0);
			var (x1, y1) = Polar(146, i * 30.0);
			g.Append($"<path d='M{F(x0)} {F(y0)}L{F(x1)} {F(y1)}'/>");
		}
		var seals = string.Concat(Enumerable.Range(0, 6).Select(i => Polar(306, i * 60.0 - 90)).Select(p => $"<circle cx='{F(p.X)}' cy='{F(p.Y)}' r='18'/>"));
		var body = $"<g transform='translate(760 440)' fill='none' stroke-width='1.5'><g stroke='{ink.Hex}' stroke-opacity='0.11'>{g}</g>"
			+ $"<g stroke='{accent.Hex}' stroke-opacity='0.22'>{seals}</g></g>";
		return Svg(body, 1280, 800, "0 0 1280 800", "xMidYMid slice");
	}

	/// <summary>
	/// An armour plate's outline with two corners cut off (top left, bottom right), drawn in place of the border, and a
	/// hazard-striped tab and rivets.
	/// </summary>
	private static string Chamfer(ThemeColor border, ThemeColor accent, ThemeColor text)
	{
		var b = $"linear-gradient({border.Hex}, {border.Hex})";
		string Cut(int angle) => $"linear-gradient({angle}deg, var(--bg) 0 calc(50% - 1px), {accent.Hex} calc(50% - 1px) calc(50% + 1px), {Rgba(accent, 0)} calc(50% + 1px))";
		var rivet = $"radial-gradient(circle, {Rgba(text, 0.4)} 0 1.5px, {Rgba(text, 0)} 2px)";
		return string.Join(", ",
			$"{Cut(135)} left top / 20px 20px no-repeat", $"{Cut(315)} right bottom / 20px 20px no-repeat",
			$"{b} right top / calc(100% - 19px) 1px no-repeat", $"{b} right top / 1px calc(100% - 19px) no-repeat",
			$"{b} left bottom / calc(100% - 19px) 1px no-repeat", $"{b} left bottom / 1px calc(100% - 19px) no-repeat",
			$"repeating-linear-gradient(-45deg, rgb(240,184,32) 0 5px, rgb(16,16,16) 5px 10px) right 30px bottom 5px / 56px 6px no-repeat",
			$"{rivet} right 5px top 5px / 6px 6px no-repeat", $"{rivet} left 5px bottom 5px / 6px 6px no-repeat");
	}
	/// <summary>The hot magenta and cyan of a toy-box cartoon, drawn alongside a theme's own accent.</summary>
	private static readonly ThemeColor ToyMagenta = new(255, 47, 180);

	private static readonly ThemeColor ToyCyan = new(34, 225, 255);

	/// <summary>A text-shadow that outlines lettering in <paramref name="ink"/>, <paramref name="width"/> em wide so it keeps to the letters' size.</summary>
	private static string InkOutline(ThemeColor ink, double width)
	{
		var w = F(width) + "em";
		return string.Join(", ", new[] { $"{w} 0", $"-{w} 0", $"0 {w}", $"0 -{w}", $"{w} {w}", $"-{w} -{w}", $"{w} -{w}", $"-{w} {w}" }
			.Select(offset => $"{offset} 0 {ink.Hex}"));
	}

	/// <summary>A brush stroke laid on in one pass, running dry at its end.</summary>
	private static string SumiBrush(ThemeColor accent) => Svg(
		$"<g fill='{accent.Hex}'><path d='M0 1.4C30 0.2 80 0 120 0.3L150 0.6L151 3.5C100 4 40 4 0 3.2Z'/>"
		+ "<rect x='148' y='0.5' width='28' height='1.1' opacity='0.8'/><rect x='150' y='2.3' width='36' height='0.9' opacity='0.55'/>"
		+ "<rect x='178' y='1' width='16' height='0.7' opacity='0.35'/></g>", 200, 4, "0 0 200 4");

	/// <summary>A red seal stamped a little askew, its carved border and strokes left in the paper's colour.</summary>
	private static string HankoSeal(ThemeColor accent) => Svg(
		$"<g transform='rotate(-6 10 10)'><rect x='2' y='2' width='16' height='16' rx='2' fill='{accent.Hex}' fill-opacity='0.85'/>"
		+ "<g fill='none' stroke='rgb(255,236,220)' stroke-opacity='0.75'><rect x='4' y='4' width='12' height='12' stroke-width='0.8'/>"
		+ "<path d='M7 6.5V13.5M13 6.5V13.5M7 10H13M9 6.5H11M9 13.5H11' stroke-width='1.1'/></g></g>", 20, 20, "0 0 20 20");

	/// <summary>A corner of shoji lattice: a run of square cells along each edge, the corner cell crossed.</summary>
	private static string KumikoCorner(ThemeColor ink) =>
		$"<g fill='none' stroke='{ink.Hex}' stroke-opacity='0.32' stroke-width='1'>"
		+ "<path d='M2 28V2H28'/><path d='M8 2V26M2 8H26M14 2V8M20 2V8M2 14H8M2 20H8'/><path d='M2 2L8 8M8 2L2 8'/></g>";

	/// <summary>A yellow caption box ruled off in ink, behind a card's heading.</summary>
	private static string CaptionBox(ThemeColor ink) => Svg(
		$"<rect width='186' height='34' fill='rgb(255,226,74)'/><path d='M0 32.5H184.5V0' fill='none' stroke='{ink.Hex}' stroke-width='3'/>", 186, 34);

	/// <summary>A halftone screen whose dots swell toward a card's corner.</summary>
	private static string BenDayFade(ThemeColor accent)
	{
		var dots = new System.Text.StringBuilder();
		for (var y = 4; y < 64; y += 8)
		{
			for (var x = 4 + (y / 8 % 2 * 4); x < 96; x += 8)
			{
				var r = 3.4 * Math.Pow((x / 96.0 + y / 64.0) / 2, 1.6);
				if (r > 0.35)
				{
					dots.Append($"<circle cx='{x}' cy='{y}' r='{F(r)}'/>");
				}
			}
		}
		return Svg($"<g fill='{accent.Hex}' fill-opacity='0.4'>{dots}</g>", 96, 64);
	}

	/// <summary>A full moon behind bands of mist, in the page's top right corner.</summary>
	private static string YokaiSky(ThemeColor ink) => Svg(
		$"<circle cx='1040' cy='130' r='150' fill='{ink.Hex}' fill-opacity='0.035'/><circle cx='1040' cy='130' r='82' fill='{ink.Hex}' fill-opacity='0.16'/>"
		+ $"<g fill='none' stroke='{ink.Hex}' stroke-opacity='0.24' stroke-width='1.6' stroke-linecap='round'>"
		+ "<path d='M1210 168H1000C978 168 976 146 994 144C1006 143 1010 156 1000 158'/>"
		+ "<path d='M1210 196H1060C1040 196 1038 178 1054 176C1064 175 1068 186 1060 188'/>"
		+ "<path d='M860 112H990C1010 112 1012 92 996 90C986 89 982 100 990 102'/>"
		+ "<path d='M780 140H900C916 140 918 124 905 122C897 121 894 130 900 132'/></g>",
		1200, 800, "0 0 1200 800", "xMaxYMin slice");

	/// <summary>Seigaiha: overlapping rings of waves along the foot of the page, fading upward.</summary>
	private static string Seigaiha(ThemeColor ink)
	{
		var waves = new System.Text.StringBuilder();
		for (var row = 0; row < 13; row++)
		{
			for (var x = -30 + (row % 2 * 30); x < 1260; x += 60)
			{
				waves.Append($"<use href='#s' x='{x}' y='{640 + row * 15}'/>");
			}
		}
		return Svg("<defs><g id='s'><circle r='30' fill='black'/><circle r='22'/><circle r='14'/><circle r='6'/></g>"
			+ "<linearGradient id='f' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='white' stop-opacity='0'/><stop offset='1' stop-color='white'/></linearGradient>"
			+ "<mask id='m'><rect y='610' width='1200' height='190' fill='url(#f)'/></mask></defs>"
			+ $"<g mask='url(#m)'><g opacity='0.2' stroke='{ink.Hex}' stroke-width='1.4' fill='none'>{waves}</g></g>",
			1200, 800, "0 0 1200 800", "xMidYMax slice");
	}

	/// <summary>Washi: long fibres caught in handmade paper.</summary>
	private static string WashiFibres(ThemeColor ink)
	{
		// Scattered by a fixed sequence, so the tile is the same on every render.
		var seed = 7u;
		double Next() => (seed = seed * 1664525u + 1013904223u) / (double)uint.MaxValue;
		var fibres = new System.Text.StringBuilder();
		for (var i = 0; i < 40; i++)
		{
			double x = Next() * 260, y = Next() * 260, a = Next() * Math.PI, len = 10 + Next() * 26, bend = (Next() - 0.5) * 16;
			double dx = Math.Cos(a) * len, dy = Math.Sin(a) * len;
			fibres.Append($"<path d='M{F(x)} {F(y)}q{F(dx / 2 - dy * bend / len)} {F(dy / 2 + dx * bend / len)} {F(dx)} {F(dy)}' stroke-opacity='{F(0.05 + Next() * 0.07)}'/>");
		}
		return Svg($"<g fill='none' stroke='{ink.Hex}' stroke-width='0.6' stroke-linecap='round'>{fibres}</g>", 260, 260);
	}

	/// <summary>Scratches and dust on an old print of a film.</summary>
	private static string FilmScratches(ThemeColor ink)
	{
		(int X, int Y, double R, double Alpha)[] dust = [(80, 210, 1.6, 0.35), (330, 90, 1, 0.3), (640, 600, 2.2, 0.25), (210, 760, 1.2, 0.3), (470, 380, 0.9, 0.35)];
		return Svg($"<g stroke='{ink.Hex}' fill='none' stroke-linecap='round'>"
			+ "<path d='M118 0C119 300 116 520 121 820' stroke-opacity='0.22' stroke-width='0.9'/>"
			+ "<path d='M402 0C400 200 405 600 403 820' stroke-opacity='0.13' stroke-width='1.6'/>"
			+ "<path d='M590 120C591 260 589 380 590 470' stroke-opacity='0.2' stroke-width='0.7'/>"
			+ "<path d='M262 520C262 600 263 700 261 820' stroke-opacity='0.12' stroke-width='0.6'/></g>"
			+ string.Concat(dust.Select(d => $"<circle cx='{d.X}' cy='{d.Y}' r='{F(d.R)}' fill='{ink.Hex}' fill-opacity='{F(d.Alpha)}'/>")), 700, 820);
	}

	/// <summary>Sparkles, burst outlines, a lightning bolt and small stars over one tile of night sky.</summary>
	private static string ToyStarbursts(ThemeColor ink, ThemeColor accent)
	{
		static string Sparkle(int x, int y, int r, ThemeColor c, double alpha) =>
			$"<path transform='translate({x} {y})' fill='{c.Hex}' fill-opacity='{F(alpha)}' d='M0 -{r}Q0 0 {r} 0Q0 0 0 {r}Q0 0 -{r} 0Q0 0 0 -{r}Z'/>";
		static string Burst(int x, int y, int outer, int inner, ThemeColor c, double alpha)
		{
			var points = string.Join(" ", Enumerable.Range(0, 24).Select(i =>
			{
				var a = i * Math.PI / 12;
				var r = i % 2 == 0 ? outer : inner;
				return $"{F(x + r * Math.Cos(a))},{F(y + r * Math.Sin(a))}";
			}));
			return $"<polygon points='{points}' fill='none' stroke='{c.Hex}' stroke-opacity='{F(alpha)}' stroke-width='2' stroke-linejoin='round'/>";
		}
		(int X, int Y)[] stars = [(20, 200), (200, 140), (500, 330), (90, 20), (260, 500), (410, 120), (330, 360), (40, 400)];
		return Svg(Sparkle(60, 80, 14, accent, 0.55) + Sparkle(300, 40, 9, ToyCyan, 0.5) + Sparkle(440, 210, 18, ToyMagenta, 0.45)
			+ Sparkle(150, 330, 10, ink, 0.4) + Sparkle(380, 440, 12, accent, 0.5) + Sparkle(250, 230, 6, ink, 0.5)
			+ Burst(110, 450, 28, 14, accent, 0.35) + Burst(470, 60, 18, 9, ToyCyan, 0.4)
			+ $"<path transform='translate(330 250) rotate(12) scale(1.6)' fill='{ToyMagenta.Hex}' fill-opacity='0.12' stroke='{accent.Hex}' stroke-opacity='0.45'"
			+ " stroke-width='1.5' stroke-linejoin='round' d='M0 0H20L12 22H24L-4 60L4 32H-8Z'/>"
			+ string.Concat(stars.Select(s => $"<circle cx='{s.X}' cy='{s.Y}' r='1.3' fill='{ink.Hex}' fill-opacity='0.55'/>")), 520, 520);
	}
	// The neon and console colours a few themes draw with beside their accent.
	private static readonly ThemeColor NeonCyan = new(0x05, 0xd9, 0xe8);
	private static readonly ThemeColor AcidYellow = new(0xf3, 0xe6, 0x00);
	private static readonly ThemeColor SunGold = new(0xff, 0xd1, 0x66);
	private static readonly ThemeColor SunOrange = new(0xff, 0x8c, 0x42);
	private static readonly ThemeColor ConsoleLavender = new(0xc3, 0x9b, 0xe0);
	private static readonly ThemeColor ConsolePeach = new(0xff, 0xb9, 0x8a);
	private static readonly ThemeColor ConsolePeriwinkle = new(0x8f, 0xa6, 0xff);

	/// <summary>The same scatter for a given seed, so a drawing never changes between resolves.</summary>
	private static Func<double> Scatter(uint seed) => () =>
	{
		seed = seed * 1664525 + 1013904223;
		return (seed >> 8) / 16777216.0;
	};

	/// <summary>Rain streaks over one tile, mostly pale, some catching the neon.</summary>
	private static string RainStreaks(ThemeColor ink, ThemeColor accent)
	{
		var next = Scatter(7);
		var body = new System.Text.StringBuilder();
		for (var i = 0; i < 34; i++)
		{
			var length = 12 + next() * 30;
			var x = length * 0.2 + next() * (260 - length * 0.2);
			var y = next() * (260 - length);
			var color = i % 7 == 0 ? accent : i % 3 == 0 ? NeonCyan : ink;
			body.Append($"<path d='M{F(x)} {F(y)}l{F(-length * 0.18)} {F(length)}' stroke='{color.Hex}' stroke-opacity='{F(0.08 + next() * 0.16)}'/>");
		}
		return Svg($"<g stroke-width='1' stroke-linecap='round'>{body}</g>", 260, 260);
	}

	/// <summary>A striped sun on the horizon over a perspective grid floor, in a sunset glow; kept to the bottom of the page.</summary>
	private static string Outrun(ThemeColor accent)
	{
		const int horizon = 540;
		var grid = new System.Text.StringBuilder();
		for (var k = 1; k <= 12; k++)
		{
			grid.Append($"<path d='M-400 {F(horizon + 260 * Math.Pow(k / 12.0, 1.8))}H1600'/>");
		}
		for (var i = -16; i <= 16; i++)
		{
			grid.Append($"<path d='M600 {horizon}L{600 + i * 110} 800'/>");
		}
		var stripes = string.Concat(new[] { (446, 3), (468, 5), (489, 7), (508, 9), (525, 11) }.Select(s => $"<rect x='0' y='{s.Item1}' width='1200' height='{s.Item2}'/>"));
		var body = "<defs>"
			+ $"<linearGradient id='s' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='{SunGold.Hex}'/><stop offset='0.5' stop-color='{SunOrange.Hex}'/><stop offset='1' stop-color='{accent.Hex}'/></linearGradient>"
			+ $"<linearGradient id='h' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='{accent.Hex}' stop-opacity='0'/><stop offset='1' stop-color='{accent.Hex}' stop-opacity='0.24'/></linearGradient>"
			+ $"<linearGradient id='f' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='{accent.Hex}' stop-opacity='0.16'/><stop offset='1' stop-color='{accent.Hex}' stop-opacity='0.02'/></linearGradient>"
			+ $"<mask id='m'><rect width='1200' height='{horizon}' fill='white'/><g fill='black'>{stripes}</g></mask></defs>"
			+ $"<rect y='280' width='1200' height='{horizon - 280}' fill='url(#h)'/>"
			+ $"<circle cx='600' cy='{horizon}' r='200' fill='url(#s)' fill-opacity='0.5' mask='url(#m)'/>"
			+ $"<rect y='{horizon}' width='1200' height='{800 - horizon}' fill='url(#f)'/>"
			+ $"<g stroke='{accent.Hex}' stroke-opacity='0.42' stroke-width='1.4' fill='none'>{grid}</g>"
			+ $"<path d='M0 {horizon}H1200' stroke='{SunGold.Hex}' stroke-opacity='0.7' stroke-width='2'/>";
		return Svg(body, 1200, 800, "0 0 1200 800", "xMidYMax slice");
	}

	/// <summary>Stars scattered over one tile, a few tinted blue, the brightest with a sparkle.</summary>
	private static string Starfield(ThemeColor ink, int size, int count, uint seed, double maxRadius)
	{
		var next = Scatter(seed);
		var body = new System.Text.StringBuilder();
		for (var i = 0; i < count; i++)
		{
			var (x, y) = (next() * size, next() * size);
			var r = 0.35 + Math.Pow(next(), 3) * maxRadius;
			var alpha = 0.3 + next() * 0.6;
			var color = i % 4 == 0 ? "#a8c4ff" : ink.Hex;
			body.Append($"<circle cx='{F(x)}' cy='{F(y)}' r='{F(r)}' fill='{color}' fill-opacity='{F(alpha)}'/>");
			if (r > 1.1)
			{
				body.Append($"<path d='M{F(x - r * 4)} {F(y)}H{F(x + r * 4)}M{F(x)} {F(y - r * 4)}V{F(y + r * 4)}' stroke='{color}' stroke-opacity='{F(alpha * 0.5)}' stroke-width='0.6'/>");
			}
		}
		return Svg(body.ToString(), size, size);
	}

	/// <summary>Coloured readout segments stacked down the right edge of the page, with a row of ticks beside them.</summary>
	private static string Readout(ThemeColor accent)
	{
		string[] colors = [accent.Hex, ConsoleLavender.Hex, ConsolePeach.Hex, ConsolePeriwinkle.Hex];
		int[] heights = [64, 28, 110, 18, 46, 86, 22, 140, 36, 58, 24, 96, 30];
		var body = new System.Text.StringBuilder("<g fill-opacity='0.17'>");
		var y = 20;
		for (var i = 0; i < heights.Length; i++)
		{
			body.Append($"<rect x='30' y='{y}' width='26' height='{heights[i]}' rx='4' fill='{colors[i % colors.Length]}'/>");
			y += heights[i] + 5;
		}
		body.Append($"</g><g stroke='{ConsolePeach.Hex}' stroke-opacity='0.14'>");
		for (var t = 24; t < 790; t += 14)
		{
			body.Append($"<path d='M{(t % 70 == 24 ? 10 : 16)} {t}H24'/>");
		}
		body.Append("</g>");
		return Svg(body.ToString(), 60, 800, "0 0 60 800", "xMaxYMin meet");
	}

	/// <summary>A cyberpunk card: two corners cut on the diagonal in neon, edges fading from neon to the border, and a glitch on the right.</summary>
	private static string GlitchFrame(ThemeColor accent, ThemeColor border) => string.Join(", ",
		$"linear-gradient(225deg, var(--bg) 0 11.3px, {accent.Hex} 11.3px 13px, {Rgba(accent, 0)} 13px) right top / 28px 28px no-repeat",
		$"linear-gradient(45deg, var(--bg) 0 11.3px, {NeonCyan.Hex} 11.3px 13px, {Rgba(NeonCyan, 0)} 13px) left bottom / 28px 28px no-repeat",
		$"linear-gradient(90deg, {accent.Hex}, {accent.Hex}) left top / 34% 2px no-repeat",
		$"linear-gradient(90deg, {accent.Hex}, {border.Hex} 60%) left top / calc(100% - 16px) 1px no-repeat",
		$"linear-gradient(180deg, {accent.Hex}, {border.Hex} 50%) left top / 1px calc(100% - 16px) no-repeat",
		$"linear-gradient({border.Hex}, {border.Hex}) right bottom / 1px calc(100% - 16px) no-repeat",
		$"linear-gradient(90deg, {NeonCyan.Hex}, {border.Hex} 40%) right bottom / calc(100% - 16px) 1px no-repeat",
		$"linear-gradient({Rgba(NeonCyan, 0.9)}, {Rgba(NeonCyan, 0.9)}) right 0 top 74% / 3px 11px no-repeat",
		$"linear-gradient({Rgba(accent, 0.9)}, {Rgba(accent, 0.9)}) right 4px top calc(74% + 14px) / 12px 2px no-repeat");

	/// <summary>A worn hull plate: rivets in the corners, a brushed grain, stains, a bevel and a painted tab.</summary>
	private static string HullPlate(ThemeColor accent)
	{
		const string rivet = "<circle cx='8' cy='8' r='2.6' fill='#5d626c' stroke='#000' stroke-opacity='0.55' stroke-width='0.7'/>"
			+ "<circle cx='7.3' cy='7.3' r='1' fill='#d0d5de' fill-opacity='0.8'/>";
		var brushed = Svg("<filter id='b'><feTurbulence type='fractalNoise' baseFrequency='0.008 0.9' numOctaves='2' stitchTiles='stitch'/>"
			+ "<feColorMatrix values='0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 0.09 0'/></filter><rect width='100%' height='100%' filter='url(#b)'/>", 300, 300);
		return string.Join(", ",
			Corners4(rivet, 16, 3),
			$"linear-gradient({accent.Hex}, {accent.Hex}) left 22px top 0 / 40px 3px no-repeat",
			"linear-gradient(180deg, rgba(255,255,255,0.06), rgba(255,255,255,0) 35%, rgba(0,0,0,0.22)) 0 0 / 100% 100% no-repeat",
			$"{brushed} 0 0 / 300px 300px",
			$"{Noise(new ThemeColor(0, 0, 0), 0.35, 0.02, 360)} 0 0 / 360px 360px");
	}

	/// <summary>A console card: a coloured elbow round the top-left corner, carried on as bars along the top and down the left.</summary>
	private static string ElbowFrame(ThemeColor accent)
	{
		var elbow = Svg($"<path fill='{accent.Hex}' d='M0 0H60V8H26A18 18 0 0 0 8 26V60H0Z'/>", 60, 60);
		string Bar(ThemeColor c) => $"linear-gradient({c.Hex}, {c.Hex})";
		return string.Join(", ",
			$"{elbow} left top / 60px 60px no-repeat",
			$"{Bar(accent)} left 60px top 0 / calc(55% - 60px) 8px no-repeat",
			$"{Bar(ConsoleLavender)} right 64px top 0 / calc(45% - 72px) 8px no-repeat",
			$"{Bar(ConsolePeach)} right 0 top 0 / 56px 8px no-repeat",
			$"{Bar(accent)} left 0 top 60px / 8px max(0px, calc(100% - 94px)) no-repeat",
			$"{Bar(ConsolePeriwinkle)} left 0 bottom 0 / 8px 30px no-repeat");
	}
	/// <summary>The signal red and yellow a real-robot mobile suit carries beside its blue.</summary>
	private static readonly ThemeColor RealRobotRed = new(214, 32, 42);

	private static readonly ThemeColor RealRobotYellow = new(247, 196, 0);

	/// <summary>A mobile suit's plating: panel lines engraved with a lit lower edge, a hatch, a no-step box, caution triangles and part-number ticks.</summary>
	private static string RealRobotPanels(ThemeColor ink, ThemeColor accent, bool dark)
	{
		const string seams = "M0 120H200L230 150H400L430 120H560M0 330H90L110 350H330L350 330H560"
			+ "M120 0V120M120 350V420M300 150V350M480 0V120M480 330V420"
			+ "M392 200H480V268L468 280H380V212Z";
		string Ticks(double x, double y, double alpha)
		{
			int[] widths = [3, 1, 1, 2, 1, 3, 1, 2, 1, 1, 3];
			var starts = widths.Select((w, i) => widths.Take(i).Sum(v => v + 1.2));
			var rects = string.Concat(widths.Zip(starts, (w, start) => $"<rect x='{F(x + start)}' y='{F(y)}' width='{w}' height='5'/>"));
			return $"<g fill='{ink.Hex}' fill-opacity='{F(alpha)}'>{rects}</g>";
		}

		string Caution(double x, double y) =>
			$"<path d='M{F(x)} {F(y + 11)}L{F(x + 6.5)} {F(y)}L{F(x + 13)} {F(y + 11)}Z' fill='{RealRobotYellow.Hex}' fill-opacity='0.8' stroke='{ink.Hex}' stroke-opacity='0.45' stroke-width='0.7'/>"
			+ $"<rect x='{F(x + 6)}' y='{F(y + 3.6)}' width='1' height='4' fill='{ink.Hex}' fill-opacity='0.7'/><rect x='{F(x + 6)}' y='{F(y + 8.4)}' width='1' height='1' fill='{ink.Hex}' fill-opacity='0.7'/>";

		var line = dark ? 0.24 : 0.2;
		var lit = dark ? 0.05 : 0.85;
		return Svg($"<g fill='none' stroke-width='1'><path d='{seams}' stroke='white' stroke-opacity='{F(lit)}' transform='translate(1 1)'/>"
			+ $"<path d='{seams}' stroke='{ink.Hex}' stroke-opacity='{F(line)}'/>"
			+ $"<circle cx='190' cy='250' r='13' stroke='{ink.Hex}' stroke-opacity='{F(line)}'/><circle cx='190' cy='250' r='9' stroke='{ink.Hex}' stroke-opacity='{F(line * 0.8)}' stroke-dasharray='2 2.7'/>"
			+ $"<rect x='150.5' y='40.5' width='110' height='50' stroke='{ink.Hex}' stroke-opacity='{F(line * 0.75)}' stroke-dasharray='5 3'/>"
			+ $"<circle cx='520' cy='190' r='5' stroke='{ink.Hex}' stroke-opacity='0.3'/></g>"
			+ $"<path d='M520 186.5L522.5 190H517.5Z' fill='{ink.Hex}' fill-opacity='0.35'/>"
			+ $"<g fill='{ink.Hex}' fill-opacity='0.22'><rect x='40' y='196' width='32' height='3' rx='1.5'/><rect x='40' y='204' width='32' height='3' rx='1.5'/><rect x='40' y='212' width='32' height='3' rx='1.5'/></g>"
			+ Caution(316, 160) + Caution(48, 360)
			+ Ticks(14, 128, 0.3) + Ticks(438, 340, 0.28) + Ticks(158, 98, 0.22)
			+ $"<rect x='400' y='286' width='16' height='3' fill='{RealRobotRed.Hex}' fill-opacity='0.6'/>"
			+ $"<rect x='308' y='300' width='3' height='3' fill='{accent.Hex}' fill-opacity='0.6'/><rect x='313' y='300' width='3' height='3' fill='{accent.Hex}' fill-opacity='0.6'/>"
			+ $"<path d='M236 360L242 352H248L242 360ZM244 360L250 352H256L250 360Z' fill='{RealRobotYellow.Hex}' fill-opacity='0.75'/>", 560, 420);
	}

	/// <summary>An armour-plate card: corners cut on the bevel, an engraved panel line inside the edge, a blue tab, a red chin plate and yellow vents.</summary>
	private static string RealRobotPlating(ThemeColor accent, ThemeColor border, ThemeColor text)
	{
		var edge = $"linear-gradient({border.Hex}, {border.Hex})";
		var panel = $"linear-gradient({Rgba(border, 0.75)}, {Rgba(border, 0.75)})";
		var bevel = Svg($"<path d='M6 0L24 18' stroke='{border.Hex}' stroke-width='1.4'/><path d='M3.9 5L19 20.1' stroke='{Rgba(border, 0.75)}' stroke-width='1'/>", 24, 24);
		var bevelTurned = Svg($"<g transform='rotate(180 12 12)'><path d='M6 0L24 18' stroke='{border.Hex}' stroke-width='1.4'/><path d='M3.9 5L19 20.1' stroke='{Rgba(border, 0.75)}' stroke-width='1'/></g>", 24, 24);
		var tab = Svg($"<path d='M0 0H50L44 6H0Z' fill='{accent.Hex}'/><path d='M54 0H66L60 6H48Z' fill='{RealRobotRed.Hex}'/>", 66, 6);
		var chin = Svg($"<path d='M0 5L4 0H30L34 5Z' fill='{RealRobotRed.Hex}'/>", 34, 5);
		var vents = Svg(string.Concat(Enumerable.Range(0, 3).Select(i =>
			$"<path d='M{F(i * 7.0 + 0.5)} 8.5L{F(i * 7.0 + 4.5)} 0.5H{F(i * 7.0 + 8)}L{F(i * 7.0 + 4)} 8.5Z' fill='{RealRobotYellow.Hex}' stroke='{Rgba(text, 0.55)}' stroke-width='0.6'/>")), 24, 9);
		var ticks = Svg(string.Concat(new[] { 0, 4, 6, 8, 12, 14, 18, 20, 22 }.Select((x, i) =>
			$"<rect x='{x}' y='0' width='{(i % 3 == 0 ? 3 : 1)}' height='5' fill='{text.Hex}' fill-opacity='0.3'/>")), 26, 5);
		return string.Join(", ",
			$"{tab} left 14px top 0 / 66px 6px no-repeat",
			$"{chin} center bottom / 34px 5px no-repeat",
			$"{vents} right 14px bottom 10px / 24px 9px no-repeat",
			$"{ticks} right 46px bottom 12px / 26px 5px no-repeat",
			$"{bevel} right top / 24px 24px no-repeat", $"{bevelTurned} left bottom / 24px 24px no-repeat",
			"linear-gradient(225deg, var(--bg) 0 50%, rgba(0,0,0,0) 50%) right top / 18px 18px no-repeat",
			"linear-gradient(45deg, var(--bg) 0 50%, rgba(0,0,0,0) 50%) left bottom / 18px 18px no-repeat",
			$"{edge} left top / calc(100% - 18px) 1px no-repeat", $"{edge} right bottom / 1px calc(100% - 18px) no-repeat",
			$"{edge} right bottom / calc(100% - 18px) 2px no-repeat", $"{edge} left top / 1px calc(100% - 18px) no-repeat",
			$"{panel} left 5px top 5px / calc(100% - 25px) 1px no-repeat", $"{panel} left 5px top 5px / 1px calc(100% - 25px) no-repeat",
			$"{panel} right 5px bottom 6px / calc(100% - 25px) 1px no-repeat", $"{panel} right 5px bottom 6px / 1px calc(100% - 26px) no-repeat",
			$"linear-gradient(180deg, {Rgba(border, 0)} 55%, {Rgba(border, 0.2)}) 0 0 / 100% 100% no-repeat");
	}
	// A super robot's colours beside the theme's accent: armour red, flame orange, gold, and a scorched red for hard shadows.
	private static readonly ThemeColor SuperRobotCrimson = new(0xd7, 0x22, 0x1c);
	private static readonly ThemeColor SuperRobotOrange = new(0xff, 0x7a, 0x1a);
	private static readonly ThemeColor SuperRobotGold = new(0xff, 0xc8, 0x2a);
	private static readonly ThemeColor SuperRobotEmber = new(0x7a, 0x0c, 0x08);

	/// <summary>A sunburst of red, orange and gold rays bursting up from below the foot of the page, fading as they rise.</summary>
	private static string SuperRobotRays()
	{
		const int cx = 600, cy = 900, reach = 1500;
		ThemeColor[] colors = [SuperRobotCrimson, SuperRobotOrange, SuperRobotGold, SuperRobotOrange];
		var rays = new System.Text.StringBuilder();
		const int count = 26;
		for (var i = 0; i < count; i++)
		{
			var from = 180 + i * 180.0 / count;
			var to = from + 180.0 / count * 0.55;
			var (x1, y1) = Polar(reach, from);
			var (x2, y2) = Polar(reach, to);
			rays.Append($"<path d='M{cx} {cy}L{F(cx + x1)} {F(cy + y1)}L{F(cx + x2)} {F(cy + y2)}Z' fill='{colors[i % colors.Length].Hex}'/>");
		}
		var body = "<defs>"
			+ $"<radialGradient id='r' gradientUnits='userSpaceOnUse' cx='{cx}' cy='{cy}' r='{reach}'><stop offset='0' stop-color='white'/>"
			+ "<stop offset='0.3' stop-color='white' stop-opacity='0.7'/><stop offset='0.62' stop-color='white' stop-opacity='0'/></radialGradient>"
			+ $"<mask id='m'><rect width='1200' height='900' fill='url(#r)'/></mask>"
			+ $"<radialGradient id='c' gradientUnits='userSpaceOnUse' cx='{cx}' cy='{cy}' r='420'><stop offset='0' stop-color='{SuperRobotGold.Hex}' stop-opacity='0.55'/>"
			+ $"<stop offset='0.45' stop-color='{SuperRobotOrange.Hex}' stop-opacity='0.22'/><stop offset='1' stop-color='{SuperRobotCrimson.Hex}' stop-opacity='0'/></radialGradient></defs>"
			+ $"<g mask='url(#m)' fill-opacity='0.2'>{rays}</g>"
			+ $"<circle cx='{cx}' cy='{cy}' r='420' fill='url(#c)'/>";
		return Svg(body, 1200, 800, "0 0 1200 800", "xMidYMax slice");
	}

	/// <summary>Embers and sparks drifting over one tile, in gold and orange.</summary>
	private static string SuperRobotEmbers()
	{
		var next = Scatter(19);
		var body = new System.Text.StringBuilder();
		for (var i = 0; i < 16; i++)
		{
			var (x, y) = (next() * 340, next() * 340);
			var color = i % 3 == 0 ? SuperRobotGold : SuperRobotOrange;
			var alpha = 0.18 + next() * 0.32;
			if (i % 5 == 0)
			{
				// A spark: a short streak flying up and out.
				body.Append($"<path d='M{F(x)} {F(y)}l{F(4 + next() * 5)} {F(-8 - next() * 8)}' stroke='{color.Hex}' stroke-opacity='{F(alpha)}' stroke-width='1.4' stroke-linecap='round'/>");
			}
			else
			{
				body.Append($"<circle cx='{F(x)}' cy='{F(y)}' r='{F(0.7 + next() * 1.3)}' fill='{color.Hex}' fill-opacity='{F(alpha)}'/>");
			}
		}
		return Svg(body.ToString(), 340, 340);
	}

	/// <summary>
	/// A super robot's armour card: a chrome trim along the top over a red band, red side plates, gold rivets in the
	/// corners, a red chest-plate chevron hanging from the band, and a sheen falling off into shadow.
	/// </summary>
	private static string SuperRobotArmor(ThemeColor accent)
	{
		var rivet = $"<circle cx='8' cy='8' r='3' fill='{SuperRobotGold.Hex}' stroke='#000' stroke-opacity='0.6' stroke-width='0.8'/>"
			+ "<circle cx='7.1' cy='7.1' r='1.1' fill='#fff6d0' fill-opacity='0.9'/>";
		var chevron = Svg($"<path d='M2 0H16L33 13L50 0H64L33 22Z' fill='{SuperRobotCrimson.Hex}' stroke='{accent.Hex}' stroke-width='1.6' stroke-linejoin='miter'/>", 66, 24);
		var chrome = "linear-gradient(180deg, #ffffff, #c3c8d3 30%, #5c616d 55%, #d9dee7 80%, #8a909c)";
		return string.Join(", ",
			Corners4(rivet, 16, 3),
			$"{chevron} right 24px top 10px / 46px 17px no-repeat",
			$"{chrome} left top / 100% 6px no-repeat",
			$"linear-gradient({SuperRobotCrimson.Hex}, {SuperRobotEmber.Hex}) left 0 top 6px / 100% 4px no-repeat",
			$"linear-gradient(90deg, {SuperRobotEmber.Hex}, {SuperRobotCrimson.Hex}) left top / 5px 100% no-repeat",
			$"linear-gradient(270deg, {SuperRobotEmber.Hex}, {SuperRobotCrimson.Hex}) right top / 5px 100% no-repeat",
			"linear-gradient(180deg, rgba(255,255,255,0.06), rgba(255,255,255,0) 30%, rgba(0,0,0,0.28)) 0 0 / 100% 100% no-repeat");
	}
}
