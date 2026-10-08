namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// What a theme sets beyond colour: its typefaces, corners, background texture, title ornament, card frame,
/// title lettering and effect, and the tone pictures take. Each is a choice from a fixed list, so a theme names a
/// look and never carries CSS of its own.
/// </summary>
/// <remarks>
/// The CSS for each choice is in <c>SharpMUSH.Client/wwwroot/css/themes/</c>, one rule per choice keyed on a data
/// attribute (<c>[data-frame-marks="plating"]</c>), with its drawings under <c>wwwroot/themes/drawings/</c>. This
/// class only names the choices and says which parts a Simple setting picks; <see cref="Parts"/> turns a theme's
/// choices into the attributes the portal puts on the page.
/// </remarks>
public static class ThemeStyles
{
	// The Simple settings: one menu each.
	public const string FontDisplay = "font-display";
	public const string FontBody = "font-body";
	public const string Corners = "corners";
	public const string Texture = "texture";
	public const string Ornament = "ornament";
	public const string Frame = "frame";
	public const string Titles = "titles";
	public const string Effect = "effect";
	public const string Imagery = "imagery";

	// The parts the Complex mode sets apart, which a Simple texture, frame, ornament or effect sets together.
	public const string TextureStrength = "texture-strength";
	public const string FrameMarks = "frame-marks";
	public const string FrameEdge = "frame-edge";
	public const string FrameShadow = "frame-shadow";
	public const string OrnamentGlyphs = "ornament-glyphs";
	public const string OrnamentUnderline = "ornament-underline";
	public const string EffectColor = "effect-color";
	public const string EffectShadow = "effect-shadow";

	/// <summary>How the theme is edited: <see cref="Simple"/>, <see cref="Complex"/> or <see cref="Custom"/>.</summary>
	public const string Mode = "mode";

	/// <summary>One menu per setting; a frame, ornament, effect or texture brings all its parts.</summary>
	public const string Simple = "simple";

	/// <summary>Each part on its own menu, plus the theme's two decorative colours.</summary>
	public const string Complex = "complex";

	/// <summary>The parts as in <see cref="Complex"/>, and the theme's own stylesheet over them.</summary>
	public const string Custom = "custom";

	public static readonly IReadOnlyList<string> Modes = [Simple, Complex, Custom];

	/// <summary>The attribute that says whether the theme is dark or light, for parts that differ between the two.</summary>
	public const string Scheme = "scheme";

	/// <summary>Faces for titles. All but <c>ui</c> are self-hosted under <c>wwwroot/fonts</c>; <c>faces.css</c> sets each one's stack and title weight.</summary>
	public static readonly IReadOnlyList<string> DisplayFaces =
	[
		"ui", "cinzel", "cormorant", "fell", "playfair", "grenze", "space", "typewriter", "orbitron", "marcellus",
		"fredoka", "dancing", "mochiy", "patrick", "dela", "graduate", "saira-stencil", "pixelify", "yuji", "bangers",
		"lilita", "titan", "chakra", "monoton", "michroma", "antonio", "oxanium", "russo",
	];

	/// <summary>Faces for everything else. Kept to faces that read well small.</summary>
	public static readonly IReadOnlyList<string> BodyFaces = ["ui", "serif"];

	/// <summary>A Simple frame's parts: the drawing on the card, its border and its shadow.</summary>
	public sealed record FrameSet(string Marks, string Edge, string Shadow);

	/// <summary>A Simple ornament's parts: the glyphs either side of a page title, and the line under it.</summary>
	public sealed record OrnamentSet(string Glyphs, string Underline);

	/// <summary>A Simple effect's parts: the colour titles are set in, and the shadow behind them.</summary>
	public sealed record EffectSet(string Color, string Shadow);

	/// <summary>The border each frame draws: <c>frames.css</c> gives each edge its style and width.</summary>
	public static readonly IReadOnlyList<string> Edges = ["line", "thick", "heavy", "double", "outset", "none"];

	/// <summary>The colour of titles: the theme's text, accent, or warning colour.</summary>
	public static readonly IReadOnlyList<string> TitleColors = ["text", "accent", "warn"];

	/// <summary>How strongly the page texture shows.</summary>
	public static readonly IReadOnlyList<string> Strengths = ["normal", "soft", "faint", "off"];

	public static readonly IReadOnlyDictionary<string, FrameSet> Frames = new Dictionary<string, FrameSet>
	{
		["plain"] = new("none", "line", "none"),
		["double"] = new("none", "double", "none"),
		["corners"] = new("corners", "line", "none"),
		["glow"] = new("none", "line", "glow"),
		["inset"] = new("none", "line", "inset"),
		["filigree"] = new("filigree", "line", "none"),
		["engraved"] = new("none", "thick", "engraved"),
		["drip"] = new("drip", "line", "drip"),
		["tape"] = new("tape", "line", "tape"),
		["bar"] = new("bar", "line", "none"),
		["deco"] = new("deco", "line", "deco"),
		["halo"] = new("halo", "line", "halo"),
		["scallop"] = new("scallop", "line", "scallop"),
		["ribbon"] = new("ribbon", "thick", "ribbon"),
		["bloom"] = new("bloom", "line", "bloom"),
		["stage"] = new("stage", "line", "stage"),
		["washi"] = new("washi", "line", "washi"),
		["panel"] = new("panel", "heavy", "none"),
		["jersey"] = new("jersey", "line", "jersey"),
		["chamfer"] = new("chamfer", "none", "none"),
		["status"] = new("status", "line", "status"),
		["shoji"] = new("shoji", "line", "shoji"),
		["inked"] = new("inked", "heavy", "inked"),
		["titlecard"] = new("titlecard", "heavy", "titlecard"),
		["chunky"] = new("chunky", "heavy", "chunky"),
		["glitch"] = new("glitch", "none", "glitch"),
		["neon"] = new("neon", "line", "neon"),
		["hull"] = new("hull", "line", "hull"),
		["elbow"] = new("elbow", "none", "none"),
		["plating"] = new("plating", "none", "none"),
		["chrome-armor"] = new("chrome-armor", "outset", "chrome-armor"),
	};

	public static readonly IReadOnlyDictionary<string, OrnamentSet> Ornaments = new Dictionary<string, OrnamentSet>
	{
		["none"] = new("none", "none"),
		["rule"] = new("none", "fade"),
		["diamond"] = new("diamond", "none"),
		["fleuron"] = new("fleuron", "fade"),
		["star"] = new("star", "none"),
		["brackets"] = new("brackets", "none"),
		["cross"] = new("cross", "none"),
		["heart"] = new("heart", "fade"),
		["lotus"] = new("lotus", "none"),
		["deco"] = new("deco", "none"),
		["double"] = new("none", "double"),
		["block"] = new("block", "none"),
		["twinkle"] = new("twinkle", "twinkle"),
		["blossom"] = new("blossom", "blossom"),
		["encore"] = new("encore", "encore"),
		["doodle"] = new("doodle", "doodle"),
		["exclaim"] = new("exclaim", "exclaim"),
		["varsity"] = new("varsity", "none"),
		["warning"] = new("warning", "none"),
		["cursor"] = new("cursor", "fade"),
		["hanko"] = new("hanko", "hanko"),
		["pow"] = new("pow", "none"),
		["vaudeville"] = new("vaudeville", "vaudeville"),
		["bolt"] = new("bolt", "bolt"),
		["slash"] = new("slash", "slash"),
		["sunset"] = new("none", "sunset"),
		["insignia"] = new("insignia", "fade"),
		["segments"] = new("segments", "segments"),
		["tricolor"] = new("tricolor", "tricolor"),
		["battlecry"] = new("battlecry", "battlecry"),
	};

	public static readonly IReadOnlyDictionary<string, EffectSet> Effects = new Dictionary<string, EffectSet>
	{
		["none"] = new("text", "none"),
		["glow"] = new("text", "glow"),
		["gilt"] = new("accent", "gilt"),
		["emboss"] = new("text", "emboss"),
		["ember"] = new("text", "ember"),
		["bleed"] = new("text", "bleed"),
		["shine"] = new("accent", "shine"),
		["dreamy"] = new("text", "dreamy"),
		["lightstick"] = new("text", "lightstick"),
		["pencil"] = new("text", "pencil"),
		["inked"] = new("text", "inked"),
		["varsity"] = new("text", "varsity"),
		["stencil"] = new("warn", "stencil"),
		["pixel"] = new("accent", "pixel"),
		["lantern"] = new("text", "lantern"),
		["inkpop"] = new("accent", "inkpop"),
		["cartoon"] = new("text", "cartoon"),
		["toybox"] = new("accent", "toybox"),
		["rgbsplit"] = new("text", "rgbsplit"),
		["retro"] = new("text", "retro"),
		["console"] = new("accent", "none"),
		["decal"] = new("accent", "decal"),
		["blazing"] = new("accent", "blazing"),
	};

	/// <summary>Every setting and part with its choices, the first being what a theme that sets none gets.</summary>
	public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Choices = new Dictionary<string, IReadOnlyList<string>>
	{
		[FontDisplay] = DisplayFaces,
		[FontBody] = BodyFaces,
		[Corners] = ["soft", "sharp", "round", "candy", "razor", "pill"],
		[Texture] =
		[
			"none", "grain", "paper", "linen", "grid", "scanlines", "stars", "mist", "damask", "lace", "velvet", "parchment",
			"foxing", "blood", "blinds", "petals", "mandala", "swiss", "sparkles", "bubbles", "spotlights", "notebook",
			"speedlines", "court", "hazard", "sigil", "sumi", "benday", "filmgrain", "starburst", "rain", "outrun", "galaxy",
			"readout", "armor-panels", "sunburst-rays",
		],
		[Ornament] = [.. Ornaments.Keys],
		[Frame] = [.. Frames.Keys],
		[Titles] = ["normal", "caps", "italic", "slant", "wide"],
		[Effect] = [.. Effects.Keys],
		[Imagery] =
		[
			"natural", "tint", "sepia", "mono", "noir", "pastel", "airy", "spotlit", "sunny", "ink", "floodlit", "steel",
			"vivid", "indigo", "print", "film", "cel", "neon", "dusk", "warm", "hangar", "hotblooded",
		],
		[TextureStrength] = Strengths,
		[FrameMarks] = [.. Frames.Values.Select(f => f.Marks).Distinct()],
		[FrameEdge] = Edges,
		[FrameShadow] = [.. Frames.Values.Select(f => f.Shadow).Distinct()],
		[OrnamentGlyphs] = [.. Ornaments.Values.Select(o => o.Glyphs).Distinct()],
		[OrnamentUnderline] = [.. Ornaments.Values.Select(o => o.Underline).Distinct()],
		[EffectColor] = TitleColors,
		[EffectShadow] = [.. Effects.Values.Select(e => e.Shadow).Distinct()],
		[Mode] = Modes,
		[ThemeVision.Key] = ThemeVision.All,
	};

	/// <summary>The Simple settings in the order the editor lists them.</summary>
	public static readonly IReadOnlyList<string> Keys = [FontDisplay, FontBody, Corners, Texture, Ornament, Frame, Titles, Effect, Imagery];

	/// <summary>The settings and parts in the order the Complex editor lists them.</summary>
	public static readonly IReadOnlyList<string> PartKeys =
	[
		FontDisplay, FontBody, Corners, Titles, Imagery,
		Texture, TextureStrength,
		FrameMarks, FrameEdge, FrameShadow,
		OrnamentGlyphs, OrnamentUnderline,
		EffectColor, EffectShadow,
	];

	/// <summary>Phosphor's look: what any setting or part a theme leaves out falls back to.</summary>
	public static readonly IReadOnlyDictionary<string, string> Defaults = Choices.ToDictionary(c => c.Key, c => c.Value[0]);

	public static bool IsValid(string key, string? value) => Choices.TryGetValue(key, out var choices) && value is not null && choices.Contains(value);

	/// <summary>The mode a theme is edited in: its own, else Custom when it has a stylesheet, else Simple.</summary>
	public static string ModeOf(IReadOnlyDictionary<string, string> tokens, string? stylesheet)
		=> tokens.TryGetValue(Mode, out var mode) && Modes.Contains(mode) ? mode
			: string.IsNullOrWhiteSpace(stylesheet) ? Simple : Custom;

	/// <summary>
	/// The part each Complex menu shows for a theme: its own where it sets one and is not Simple, else the part its
	/// Simple setting brings.
	/// </summary>
	public static Dictionary<string, string> PartsOf(IReadOnlyDictionary<string, string> style, string mode)
	{
		string Pick(string key) => style.TryGetValue(key, out var v) && IsValid(key, v) ? v : Defaults[key];
		string Own(string key, string fromSimple) => mode != Simple && style.TryGetValue(key, out var v) && IsValid(key, v) ? v : fromSimple;

		var frame = Frames[Pick(Frame)];
		var ornament = Ornaments[Pick(Ornament)];
		var effect = Effects[Pick(Effect)];
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[FontDisplay] = Pick(FontDisplay),
			[FontBody] = Pick(FontBody),
			[Corners] = Pick(Corners),
			[Titles] = Pick(Titles),
			[Imagery] = Pick(Imagery),
			[Texture] = Pick(Texture),
			[TextureStrength] = Own(TextureStrength, Defaults[TextureStrength]),
			[FrameMarks] = Own(FrameMarks, frame.Marks),
			[FrameEdge] = Own(FrameEdge, frame.Edge),
			[FrameShadow] = Own(FrameShadow, frame.Shadow),
			[OrnamentGlyphs] = Own(OrnamentGlyphs, ornament.Glyphs),
			[OrnamentUnderline] = Own(OrnamentUnderline, ornament.Underline),
			[EffectColor] = Own(EffectColor, effect.Color),
			[EffectShadow] = Own(EffectShadow, effect.Shadow),
		};
	}

	/// <summary>
	/// The data attributes the portal sets for a theme (without <c>data-</c>): one per part, and <see cref="Scheme"/>.
	/// The rules in <c>css/themes/</c> match these.
	/// </summary>
	public static Dictionary<string, string> Parts(IReadOnlyDictionary<string, string> style, string mode, bool dark)
	{
		var parts = PartsOf(style, mode);
		parts[Scheme] = dark ? "dark" : "light";
		return parts;
	}
}
