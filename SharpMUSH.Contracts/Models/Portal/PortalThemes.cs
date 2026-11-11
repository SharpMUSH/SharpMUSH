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
	public const string Accent2 = "accent-2";
	public const string Accent3 = "accent-3";

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

	/// <summary>
	/// Two more colours a theme may set, for the drawings in its texture, frame and ornament only: they carry no
	/// text, so no contrast pair checks them. A theme that leaves one out gets its <see cref="LinkMissing"/> and
	/// <see cref="Warn"/> colours.
	/// </summary>
	public static readonly IReadOnlyList<string> Decorative = [Accent2, Accent3];

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

	/// <summary>
	/// The portal's dark look: the phosphor accent on warm charcoal, titles in Space Grotesk, a faint grain, and cards
	/// lifted off the page. <c>tokens.css</c> carries the same colours.
	/// </summary>
	public static readonly PortalTheme Phosphor = new(PhosphorId, "Phosphor", Dark: true, Published: true,
		WithEmberStyle(new Dictionary<string, string>
		{
			[ThemeTokens.Background] = "#121110",
			[ThemeTokens.Surface] = "#191716",
			[ThemeTokens.Surface2] = "#211e1c",
			[ThemeTokens.Surface3] = "#161412",
			[ThemeTokens.Rail] = "#0c0b0a",
			[ThemeTokens.Text] = "#efebe5",
			[ThemeTokens.TextDim] = "#aaa298",
			[ThemeTokens.TextFaint] = "#90887e",
			[ThemeTokens.Border] = "#322d28",
			[ThemeTokens.BorderSoft] = "#272320",
			[ThemeTokens.Accent] = "#00f5b7",
			[ThemeTokens.Warn] = "#ffb454",
			[ThemeTokens.LinkMissing] = "#ff8a8a",
		}), BuiltIn: true);

	/// <summary>The light look, Phosphor's on warm paper, so a game has one to start from and the portal is exercised in both modes.</summary>
	public static readonly PortalTheme Daylight = new(DaylightId, "Daylight", Dark: false, Published: true,
		WithEmberStyle(new Dictionary<string, string>
		{
			[ThemeTokens.Background] = "#f6f4ef",
			[ThemeTokens.Surface] = "#ffffff",
			[ThemeTokens.Surface2] = "#f3f0e9",
			[ThemeTokens.Surface3] = "#f0ebe5",
			[ThemeTokens.Rail] = "#e6dfd3",
			[ThemeTokens.Text] = "#1c1915",
			[ThemeTokens.TextDim] = "#524b43",
			[ThemeTokens.TextFaint] = "#655d54",
			[ThemeTokens.Border] = "#dcd4c8",
			[ThemeTokens.BorderSoft] = "#e8e1d6",
			[ThemeTokens.Accent] = "#00775f",
			[ThemeTokens.Warn] = "#8f5300",
			[ThemeTokens.LinkMissing] = "#c0362c",
		}), BuiltIn: true);

	/// <summary>Phosphor's and Daylight's look beyond colour: Space Grotesk titles, grain, lifted cards.</summary>
	private static Dictionary<string, string> WithEmberStyle(Dictionary<string, string> tokens)
	{
		tokens[ThemeStyles.FontDisplay] = "space";
		tokens[ThemeStyles.Texture] = "grain";
		tokens[ThemeStyles.Frame] = "lifted";
		return tokens;
	}

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
		display: "typewriter", body: "ui", corners: "sharp", texture: "blinds", ornament: "rule", frame: "tape", titles: "normal", effect: "bleed", imagery: "noir",
		accent2: "#e2d6ba");

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

	/// <summary>Magical girl: candy pink and lavender, twinkling sparkles, a rounded face that shines, cards tied with a bow.</summary>
	public static readonly PortalTheme MagicalGirl = Genre("magical-girl", "Magical Girl", dark: false,
		["#fcedf7", "#fffaff", "#f9eafa", "#f3e1f5", "#efd8f3", "#2e1538", "#614670", "#6b5279", "#ecc9ee", "#f5e0f6", "#b8157a", "#8f4b00", "#b3261e"],
		display: "fredoka", body: "ui", corners: "candy", texture: "sparkles", ornament: "twinkle", frame: "ribbon", titles: "normal", effect: "shine", imagery: "pastel", accent2: "#966edc");

	/// <summary>Shojo: a cream manga page in screentone, floating bubbles and big blossoms, script titles, flower-sprayed cards.</summary>
	public static readonly PortalTheme Shojo = Genre("shojo", "Shojo", dark: false,
		["#fdf8f1", "#fffdf9", "#fbf3ea", "#f6ece2", "#f2e5d8", "#2b2226", "#5e4f55", "#695a60", "#ecd9cf", "#f4e9e1", "#0f6f73", "#8f4b00", "#b3261e"],
		display: "dancing", body: "ui", corners: "round", texture: "bubbles", ornament: "blossom", frame: "bloom", titles: "normal", effect: "dreamy", imagery: "airy", accent2: "#ee7a70", accent3: "#966edc");

	/// <summary>Idol: a concert stage in indigo dark, coloured spotlights, pop lettering glowing like a lightstick, cards rimmed in stage lights.</summary>
	public static readonly PortalTheme Idol = Genre("idol", "Idol", dark: true,
		["#0c0a1d", "#151030", "#1c163f", "#100c25", "#07051a", "#f4efff", "#bbb2da", "#9e94c4", "#30275f", "#221b4a", "#ff58a8", "#ffd84a", "#ff8a8a"],
		display: "mochiy", body: "ui", corners: "soft", texture: "spotlights", ornament: "encore", frame: "stage", titles: "normal", effect: "lightstick", imagery: "spotlit", accent2: "#36e2ff", accent3: "#ffde4a");

	/// <summary>Slice of life: warm daylight on ruled notebook paper, a handwritten face, cards stuck up with patterned washi tape.</summary>
	public static readonly PortalTheme SliceOfLife = Genre("slice-of-life", "Slice of Life", dark: false,
		["#fbf7ec", "#fffefa", "#f8f3e6", "#f3ecdc", "#ece3cf", "#2a2722", "#57514a", "#615a51", "#e2d8c3", "#eee6d5", "#1f68a8", "#8f4b00", "#b3261e"],
		display: "patrick", body: "ui", corners: "soft", texture: "notebook", ornament: "doodle", frame: "washi", titles: "normal", effect: "pencil", imagery: "sunny", accent2: "#ffd678", accent3: "#80ccb2");
	/// <summary>Shonen: a battle-manga page, black ink on white with screentone and focus lines, heavy slanted titles, thick panel borders and one hot red.</summary>
	public static readonly PortalTheme Shonen = Genre("shonen", "Shonen", dark: false,
		["#f4f2ee", "#ffffff", "#f0eeea", "#e8e6e1", "#e2dfda", "#0d0d0d", "#3a3a3a", "#545454", "#141414", "#d4d1cb", "#c42d12", "#8f4b00", "#b3261e"],
		display: "dela", body: "ui", corners: "razor", texture: "speedlines", ornament: "exclaim", frame: "panel", titles: "slant", effect: "inked", imagery: "ink");

	/// <summary>Sports: a night match under floodlights, court markings on navy, varsity lettering outlined in jersey orange, striped hems.</summary>
	public static readonly PortalTheme Sports = Genre("sports", "Sports", dark: true,
		["#0a1428", "#101d36", "#162644", "#0c172e", "#060d1c", "#eef3fb", "#a9b6cc", "#8d9bb4", "#25395f", "#1a2b4b", "#ff8c2e", "#ffd166", "#ff8a8a"],
		display: "graduate", body: "ui", corners: "soft", texture: "court", ornament: "varsity", frame: "jersey", titles: "caps", effect: "varsity", imagery: "floodlit");

	/// <summary>Mech machine: a battle mech's hangar of riveted gunmetal plates, hazard stripes, stencilled capitals in warning yellow, cut-corner armour cards.</summary>
	public static readonly PortalTheme MechMachine = Genre("mechmachine", "Mech Machine", dark: true,
		["#16181b", "#1f2226", "#272b30", "#1a1c20", "#101113", "#e7e9eb", "#aab0b6", "#8f969d", "#3c434b", "#2b3036", "#ff6b1f", "#ffc62a", "#ff8a7a"],
		display: "saira-stencil", body: "ui", corners: "razor", texture: "hazard", ornament: "warning", frame: "chamfer", titles: "caps", effect: "stencil", imagery: "steel",
		accent2: "#f0b820");

	/// <summary>Isekai: a game's status windows over a fantasy world, royal-blue panels edged in light, a magic circle behind, pixel titles in gold.</summary>
	public static readonly PortalTheme Isekai = Genre("isekai", "Isekai", dark: true,
		["#0a0e2c", "#121a4a", "#18225a", "#0e1540", "#070a22", "#eaf0ff", "#acb8e4", "#919dd0", "#4466cc", "#24347c", "#ffd35e", "#ffb35c", "#ff9aa8"],
		display: "pixelify", body: "ui", corners: "sharp", texture: "sigil", ornament: "cursor", frame: "status", titles: "normal", effect: "pixel", imagery: "vivid");
	/// <summary>Yokai: a folklore night in sumi and indigo, a moon in mist over waves, brushed titles sealed in vermilion, shoji-latticed cards.</summary>
	public static readonly PortalTheme Yokai = Genre("yokai", "Yokai", dark: true,
		["#0c0e1b", "#141728", "#1b1f34", "#0f111f", "#080913", "#efe6d4", "#b3ab9b", "#958d80", "#2b2e47", "#1e2135", "#f2643c", "#e8b04a", "#ff9580"],
		display: "yuji", body: "ui", corners: "sharp", texture: "sumi", ornament: "hanko", frame: "shoji", titles: "normal", effect: "lantern", imagery: "indigo",
		accent2: "#ffecdc");

	/// <summary>Comic book: four-colour newsprint in Ben-Day dots, inked panels with hard shadows, outlined capitals that shout.</summary>
	public static readonly PortalTheme ComicBook = Genre("comic-book", "Comic Book", dark: false,
		["#f5eed6", "#fffdf4", "#fff3c4", "#fff1b8", "#1f57d6", "#111111", "#333333", "#4d4d4d", "#111111", "#dccfae", "#c8141c", "#8a5200", "#a3121a"],
		display: "bangers", body: "ui", corners: "sharp", texture: "benday", ornament: "pow", frame: "inked", titles: "caps", effect: "inkpop", imagery: "print", accent2: "#ffe24a", accent3: "#0096d6");

	/// <summary>Rubber hose: a 1930s cartoon title card, cream and ink on scratched, grainy film, bouncy letters between stars.</summary>
	public static readonly PortalTheme RubberHose = Genre("rubber-hose", "Rubber Hose", dark: false,
		["#ebe1c6", "#f7efd8", "#ede2c2", "#e6d9b5", "#221d19", "#1c1814", "#463d33", "#564b3f", "#221d19", "#d3c39d", "#a3302a", "#7a4d00", "#9c2a1f"],
		display: "lilita", body: "ui", corners: "round", texture: "filmgrain", ornament: "vaudeville", frame: "titlecard", titles: "normal", effect: "cartoon", imagery: "film");

	/// <summary>80s cartoon: a Saturday-morning toy-box sky of star bursts and lightning, chunky outlined yellow letters, cards with hard magenta shadows.</summary>
	public static readonly PortalTheme EightiesCartoon = Genre("eighties-cartoon", "80s Cartoon", dark: true,
		["#190e3c", "#24164f", "#2e1d63", "#1d1146", "#10082a", "#fff6ff", "#d3c2ee", "#b3a1d6", "#6a4bd6", "#33216e", "#ffe23a", "#ffa94d", "#ff86c2"],
		display: "titan", body: "ui", corners: "round", texture: "starburst", ornament: "bolt", frame: "chunky", titles: "caps", effect: "toybox", imagery: "cel", accent2: "#ff2fb4", accent3: "#22e1ff");
	/// <summary>Cyberpunk: a neon city at night, rain past magenta and cyan signs, split-signal capitals, cards cut on the diagonal.</summary>
	public static readonly PortalTheme Cyberpunk = Genre("cyberpunk", "Cyberpunk", dark: true,
		["#07060b", "#110d17", "#19131f", "#0c0a11", "#040307", "#f2edf7", "#b8acc6", "#9d90ad", "#2e2339", "#1e1727", "#ff2bd6", "#f3e600", "#ff8a8a"],
		display: "chakra", body: "ui", corners: "sharp", texture: "rain", ornament: "slash", frame: "glitch", titles: "caps", effect: "rgbsplit", imagery: "neon",
		accent2: "#05d9e8", accent3: "#f3e600");

	/// <summary>Synthwave: an outrun sunset, a striped sun over a neon grid, chrome-lit inline lettering, cards lit like neon tubes.</summary>
	public static readonly PortalTheme Synthwave = Genre("synthwave", "Synthwave", dark: true,
		["#1a0b2e", "#25103f", "#311552", "#1d0c35", "#12061f", "#fdeeff", "#d8bfe6", "#bfa2d2", "#4a2470", "#331856", "#ff6b9d", "#ffd166", "#ff9a8a"],
		display: "monoton", body: "ui", corners: "round", texture: "outrun", ornament: "sunset", frame: "neon", titles: "normal", effect: "retro", imagery: "dusk",
		accent2: "#ffd166", accent3: "#ff8c42");

	/// <summary>Space opera: a deep starfield with a planet on the horizon, wide gold capitals, worn riveted hull plates.</summary>
	public static readonly PortalTheme SpaceOpera = Genre("space-opera", "Space Opera", dark: true,
		["#04050b", "#14161d", "#1c1f28", "#0d0f15", "#03040a", "#eee7d4", "#b9b09a", "#9a927f", "#3b3d47", "#252731", "#f0b429", "#ff8c42", "#ff8a7a"],
		display: "michroma", body: "ui", corners: "soft", texture: "galaxy", ornament: "insignia", frame: "hull", titles: "wide", effect: "gilt", imagery: "warm",
		accent2: "#8cb4ff", accent3: "#ffe2be");

	/// <summary>Starship console: pure black, tall condensed capitals, pill bars and elbows in orange, lavender, peach and periwinkle.</summary>
	public static readonly PortalTheme StarshipConsole = Genre("starship-console", "Starship Console", dark: true,
		["#000000", "#0b0a0f", "#16141c", "#060509", "#000000", "#f5e9da", "#cbbba8", "#a99a8b", "#2b2732", "#1a1820", "#ff9933", "#ffcc66", "#ff8a7a"],
		display: "antonio", body: "ui", corners: "pill", texture: "readout", ornament: "segments", frame: "elbow", titles: "caps", effect: "console", imagery: "natural",
		accent2: "#c39be0", accent3: "#8fa6ff");

	/// <summary>Real robot: a military mobile suit's white armour plating with panel lines and decals, blue with signal red and yellow, beveled plate cards.</summary>
	public static readonly PortalTheme RealRobot = Genre("real-robot", "Real Robot", dark: false,
		["#e4e8ee", "#fcfdfe", "#f0f3f7", "#e9edf2", "#1b3478", "#121a28", "#3b4658", "#4f5a6b", "#a3aebd", "#d3dae3", "#1a49b5", "#8a5a00", "#b8141e"],
		display: "oxanium", body: "ui", corners: "sharp", texture: "armor-panels", ornament: "tricolor", frame: "plating", titles: "caps", effect: "decal", imagery: "hangar", accent2: "#d6202a", accent3: "#f7c400");

	/// <summary>Super robot: a 1970s giant robot's finishing move, gold capitals blazing over a red and gold sunburst, cards of chrome-trimmed red armour.</summary>
	public static readonly PortalTheme SuperRobot = Genre("super-robot", "Super Robot", dark: true,
		["#160609", "#210c10", "#2c1116", "#19080b", "#080a1c", "#fff3e3", "#e0bfb0", "#c09a8e", "#8a8f9d", "#3a1a1e", "#ffc82a", "#ff9a3c", "#ff8f80"],
		display: "russo", body: "ui", corners: "sharp", texture: "sunburst-rays", ornament: "battlecry", frame: "chrome-armor", titles: "slant", effect: "blazing", imagery: "hotblooded", accent2: "#ff7a1a", accent3: "#d7221c");

	/// <summary>Heart Realm: a night sky over a stained-glass platform, regal gold capitals, cards banded in coloured glass, a crown either side of a title.</summary>
	public static readonly PortalTheme HeartRealm = Genre("heart-realm", "Heart Realm", dark: true,
		["#0b0b24", "#14143a", "#1c1c4a", "#10102e", "#07071a", "#f3efff", "#b9b4dc", "#9a95c2", "#3a3a7a", "#262658", "#ffd447", "#ffb35c", "#ff8fa8"],
		display: "cinzel-decorative", body: "ui", corners: "round", texture: "stained-glass", ornament: "crown", frame: "stained", titles: "normal", effect: "starlit", imagery: "dreamlit",
		accent2: "#5cc8ff", accent3: "#ff5d8f");

	/// <summary>Crystal Epic: a modern fantasy epic, crystal shards rising out of the dark, wide Roman capitals, glassy cards lit from above.</summary>
	public static readonly PortalTheme CrystalEpic = Genre("crystal-epic", "Crystal Epic", dark: true,
		["#070b14", "#0f1626", "#16203a", "#0b111f", "#04070d", "#e8eef8", "#a6b3c8", "#8796ad", "#2a3a58", "#1b2740", "#9cd8ff", "#f2c66d", "#ff8f9a"],
		display: "forum", body: "ui", corners: "soft", texture: "crystal", ornament: "crystal", frame: "prism", titles: "wide", effect: "prism", imagery: "crystalline",
		accent2: "#d4ecff", accent3: "#b6a2ff");

	/// <summary>Crystal Epic 16-bit: a 16-bit console RPG, blue gradient menu windows with white borders on black, pixel capitals, a pointing glove.</summary>
	public static readonly PortalTheme CrystalEpic16Bit = Genre("crystal-epic-16bit", "Crystal Epic 16-bit", dark: true,
		["#02020c", "#1a2a9a", "#2236b0", "#101c70", "#000010", "#ffffff", "#d6dcff", "#b8c0f0", "#e8e8f0", "#5868c8", "#ffd85a", "#ffb070", "#ffb0bc"],
		display: "press-start", body: "ui", corners: "sharp", texture: "dither", ornament: "glove", frame: "window", titles: "normal", effect: "bevel", imagery: "sixteen-bit",
		accent2: "#5f78ff", accent3: "#c8c8dc");

	/// <summary>Calling Card: a phantom thief's calling card, black and white torn through with red, halftone dots, tall slanted capitals.</summary>
	public static readonly PortalTheme CallingCard = Genre("calling-card", "Calling Card", dark: true,
		["#0a0a0a", "#141414", "#1e1e1e", "#0f0f0f", "#000000", "#ffffff", "#c8c8c8", "#a3a3a3", "#3a3a3a", "#262626", "#ff3344", "#ffd400", "#ff8a8a"],
		display: "anton", body: "ui", corners: "razor", texture: "shards", ornament: "jagged", frame: "torn", titles: "slant", effect: "ransom", imagery: "redout",
		accent2: "#f5f5f5", accent3: "#ffd400");

	/// <summary>JRPG: a classic console role-playing game, black dialog boxes with white borders over an overworld of tiles, dotted lettering, a cursor before a title.</summary>
	public static readonly PortalTheme Jrpg = Genre("jrpg", "JRPG", dark: true,
		["#05070a", "#000000", "#14161c", "#000000", "#000000", "#ffffff", "#d0d0d0", "#a8a8a8", "#f2f2f2", "#3a3a3a", "#ffb22e", "#ffe066", "#ff8a8a"],
		display: "dotgothic", body: "ui", corners: "soft", texture: "overworld", ornament: "pointer", frame: "dialog", titles: "normal", effect: "dialog", imagery: "cartridge",
		accent2: "#58c46a", accent3: "#4aa8ff");

	/// <summary>Adventurer's Handbook: a tabletop rulebook, a cream page washed with colour, red small-capital headings over a tapered gold rule, stat-block cards.</summary>
	public static readonly PortalTheme AdventurersHandbook = Genre("adventurers-handbook", "Adventurer's Handbook", dark: false,
		["#f3ead3", "#fdf8ea", "#f6edd6", "#ede2c6", "#4a1810", "#22180e", "#4f3f2d", "#5f4d39", "#cdb88c", "#e2d4b2", "#7e1b0f", "#7f4f00", "#a32a1d"],
		display: "alegreya-sc", body: "serif", corners: "sharp", texture: "watercolor", ornament: "rubric", frame: "statblock", titles: "normal", effect: "rubric", imagery: "watercolor",
		accent2: "#c9a243", accent3: "#e69a28");

	/// <summary>Trailblazer: a tabletop rulebook's vellum page in a stone border, condensed capitals in maroon, action diamonds, cards headed like a stat block.</summary>
	public static readonly PortalTheme Trailblazer = Genre("trailblazer", "Trailblazer", dark: false,
		["#e9e4d8", "#fbf8f1", "#f2ede2", "#e8e2d4", "#1d2433", "#1b1a17", "#45413a", "#57524a", "#b9b0a0", "#dcd5c7", "#6b0f0f", "#7a4d00", "#a32a1d"],
		display: "oswald", body: "ui", corners: "sharp", texture: "marble", ornament: "actions", frame: "ledger", titles: "caps", effect: "stamped", imagery: "etched",
		accent2: "#c99a2e", accent3: "#2b4a7a");

	// One pair per colour vision (ThemeVision), Phosphor's and Daylight's surfaces and text with an accent, warning and
	// missing-link colour that stay apart for that vision. ThemeVision picks the pair for a player who names a vision
	// and no theme, and swaps the status and code colours to match. Colours: accent, warn, link-missing.

	/// <summary>Protan dark: for red-weak and red-blind readers; sky accent, yellow warnings, orange errors.</summary>
	public static readonly PortalTheme ProtanDark = Vision(ThemeVision.Protan, "Protan Dark", dark: true, "#a8e8fa", "#f2ef0a", "#ec8c2d");

	/// <summary>Protan light: for red-weak and red-blind readers; ocean accent, ochre warnings, brown errors.</summary>
	public static readonly PortalTheme ProtanLight = Vision(ThemeVision.Protan, "Protan Light", dark: false, "#1b729b", "#907300", "#732104");

	/// <summary>Deutan dark: for green-weak and green-blind readers; sky accent, yellow warnings, salmon errors.</summary>
	public static readonly PortalTheme DeutanDark = Vision(ThemeVision.Deutan, "Deutan Dark", dark: true, "#b6edfa", "#fde30b", "#efa083");

	/// <summary>Deutan light: for green-weak and green-blind readers; deep blue accent, ochre warnings, dark red errors.</summary>
	public static readonly PortalTheme DeutanLight = Vision(ThemeVision.Deutan, "Deutan Light", dark: false, "#045781", "#897608", "#770b04");

	/// <summary>Tritan dark: for blue-weak and blue-blind readers; pink accent, cream warnings, red errors.</summary>
	public static readonly PortalTheme TritanDark = Vision(ThemeVision.Tritan, "Tritan Dark", dark: true, "#ff93c4", "#fce49e", "#fc1f0a");

	/// <summary>Tritan light: for blue-weak and blue-blind readers; crimson accent, ochre warnings, dark red errors.</summary>
	public static readonly PortalTheme TritanLight = Vision(ThemeVision.Tritan, "Tritan Light", dark: false, "#d10759", "#967102", "#6e0700");

	/// <summary>Mono dark: for readers who see no colour; each meaningful colour a different lightness.</summary>
	public static readonly PortalTheme MonoDark = Vision(ThemeVision.Mono, "Mono Dark", dark: true, "#318ec0", "#e4e2c8", "#ebccc6");

	/// <summary>Mono light: for readers who see no colour; each meaningful colour a different lightness.</summary>
	public static readonly PortalTheme MonoLight = Vision(ThemeVision.Mono, "Mono Light", dark: false, "#193055", "#7f7833", "#916862");

	/// <summary>Gothic punk: a city of spires and gargoyles black against a blood-red sky in the rain, blackletter titles, cards raked by claws.</summary>
	public static readonly PortalTheme GothicPunk = Genre("gothic-punk", "Gothic Punk", dark: true,
		["#0d0b0c", "#171315", "#211b1e", "#120f11", "#070506", "#ece4dc", "#b8aca3", "#968a82", "#4a3a3f", "#2a2024", "#ef5a70", "#d9a441", "#ff8a8a"],
		display: "pirata", body: "ui", corners: "sharp", texture: "spires", ornament: "thorn", frame: "talon", titles: "normal", effect: "bloodlet", imagery: "nocturne",
		accent2: "#8fa58c", accent3: "#c9b99a");

	/// <summary>Dragonrider: bronze, green and blue dragons wheeling over the cliffs of their weyr while silver threads fall, uncial titles, cards edged in scales.</summary>
	public static readonly PortalTheme Dragonrider = Genre("dragonrider", "Dragonrider", dark: true,
		["#120f0c", "#1b1612", "#251e18", "#15110e", "#0a0806", "#f1e8dc", "#c2b4a2", "#9e907f", "#5a4630", "#2e241b", "#e0a458", "#f2c14e", "#ff9a8a"],
		display: "uncial", body: "ui", corners: "soft", texture: "dragonflight", ornament: "wing", frame: "scales", titles: "normal", effect: "firestone", imagery: "duskfire",
		accent2: "#5fc28f", accent3: "#6fa8f0");

	/// <summary>Fleet Command: a warship's plotting table, amber contacts on a sweeping scope, condensed capitals, cards with their corners clipped like fleet paperwork.</summary>
	public static readonly PortalTheme FleetCommand = Genre("fleet-command", "Fleet Command", dark: true,
		["#0e1012", "#171a1d", "#1f2327", "#121417", "#08090a", "#e6e8e6", "#aeb3b5", "#8a9093", "#4a5056", "#262a2e", "#f2a93b", "#ffd166", "#ff8f80"],
		display: "barlow", body: "ui", corners: "sharp", texture: "plot", ornament: "chevron", frame: "clipped", titles: "caps", effect: "scope", imagery: "gunmetal",
		accent2: "#7fb0d9", accent3: "#c9c2b0");

	/// <summary>Gaslight Occult: a Victorian séance, a brass ritual chart with an open eye glowing out of the dark, mystic titles, cards marked with the moon and sun.</summary>
	public static readonly PortalTheme Gaslight = Genre("gaslight", "Gaslight Occult", dark: true,
		["#120e0b", "#1c1612", "#261e18", "#16110d", "#0a0806", "#efe6d6", "#c0b29b", "#9c8f7b", "#5c4a33", "#2e241a", "#d6b25e", "#e3a04f", "#ff9b8a"],
		display: "metamorphous", body: "ui", corners: "soft", texture: "seance", ornament: "moon", frame: "planchette", titles: "normal", effect: "ectoplasm", imagery: "daguerreotype",
		accent2: "#8fbf8a", accent3: "#c0574a");

	/// <summary>Desert planet: dune ridges under two moons and a spice-orange dusk, wide geometric capitals, cards topped with a stepped band.</summary>
	public static readonly PortalTheme DesertPlanet = Genre("desert-planet", "Desert Planet", dark: true,
		["#17100a", "#211710", "#2c1f15", "#1a120c", "#0d0905", "#f5e9d6", "#cdb79a", "#a8937a", "#6b4e30", "#34261a", "#f0913a", "#f2c14e", "#ff9b8a"],
		display: "krona", body: "ui", corners: "sharp", texture: "dunes", ornament: "sun-disc", frame: "sandstep", titles: "caps", effect: "heat", imagery: "sunbleached",
		accent2: "#4da3e6", accent3: "#e3c9a0");

	/// <summary>Arcane academy: candles floating under an enchanted night ceiling, a calligraphic hand, cards bearing a house crest in gold.</summary>
	public static readonly PortalTheme ArcaneAcademy = Genre("arcane-academy", "Arcane Academy", dark: true,
		["#0d0f1c", "#151829", "#1d2136", "#11132a", "#070814", "#f0ecdf", "#bdb7a6", "#9a9586", "#4a4566", "#262a42", "#e6c35c", "#f0a35a", "#ff9aa2"],
		display: "eagle-lake", body: "ui", corners: "round", texture: "candles", ornament: "wand", frame: "crest", titles: "normal", effect: "spellglow", imagery: "candlelit",
		accent2: "#c24a58", accent3: "#4fa37b");

	/// <summary>Brass and steam: copper and verdigris gears turning in the corners over steam pipes, ornate titles, riveted brass plates for cards.</summary>
	public static readonly PortalTheme Steampunk = Genre("steampunk", "Brass and Steam", dark: true,
		["#15100b", "#1f1811", "#2a2017", "#18120d", "#0b0805", "#f0e4cf", "#c4b193", "#a08e72", "#7a5a34", "#352718", "#d9954a", "#e8c25a", "#ff9b8a"],
		display: "federant", body: "ui", corners: "soft", texture: "gears", ornament: "cog", frame: "riveted", titles: "normal", effect: "brass", imagery: "tintype",
		accent2: "#a8b4ad", accent3: "#5f9e8f");

	/// <summary>Frontier: a wanted poster in the desert, mesas and saguaros under a low sun, wood-type titles, cards nailed up at the corners.</summary>
	public static readonly PortalTheme Frontier = Genre("frontier", "Frontier", dark: false,
		["#ece0c3", "#f8f0dc", "#f1e6cb", "#e6d9ba", "#2b1d10", "#2a1c10", "#54412d", "#644f39", "#b8a07a", "#dccdab", "#8a2a17", "#7a4d00", "#a32a1d"],
		display: "rye", body: "ui", corners: "sharp", texture: "mesa", ornament: "sheriff", frame: "wanted", titles: "normal", effect: "woodtype", imagery: "sunbaked",
		accent2: "#6b4a24", accent3: "#2f5f6f");

	/// <summary>Wasteland: the broken skyline of a dead city in a yellow dust haze, stencilled military capitals, scrap cards with hazard stripes.</summary>
	public static readonly PortalTheme Wasteland = Genre("wasteland", "Wasteland", dark: true,
		["#12110d", "#1b1a14", "#24221a", "#16150f", "#0a0907", "#ebe6d4", "#bab49e", "#96907c", "#5a5340", "#2c2a20", "#e0c93a", "#e8893a", "#ff9b8a"],
		display: "black-ops", body: "ui", corners: "sharp", texture: "ruins", ornament: "rad", frame: "scrap", titles: "caps", effect: "grime", imagery: "irradiated",
		accent2: "#9bb55a", accent3: "#c0653a");

	public static readonly IReadOnlyList<PortalTheme> All =
	[
		Phosphor, Daylight, Fantasy, Historical, Horror, Modern, Mystery, Romance, ScienceFiction, Spiritual,
		MagicalGirl, Shojo, Idol, SliceOfLife,
		Shonen, Sports, MechMachine, Isekai,
		Yokai, ComicBook, RubberHose, EightiesCartoon,
		Cyberpunk, Synthwave, SpaceOpera, StarshipConsole,
		RealRobot,
		SuperRobot,
		HeartRealm, CrystalEpic, CrystalEpic16Bit, CallingCard, Jrpg,
		AdventurersHandbook, Trailblazer,
		GothicPunk, Dragonrider, FleetCommand, Gaslight, DesertPlanet,
		ArcaneAcademy, Steampunk, Frontier, Wasteland,
		ProtanDark, ProtanLight, DeutanDark, DeutanLight, TritanDark, TritanLight, MonoDark, MonoLight,
	];

	/// <summary>
	/// A colour vision theme: Phosphor or Daylight with its own accent, warning and missing-link colours, and no grain.
	/// Its colours reach 4.5:1 against the flat surfaces only, so nothing may be drawn between them and the text.
	/// </summary>
	private static PortalTheme Vision(string vision, string name, bool dark, string accent, string warn, string linkMissing)
	{
		var tokens = new Dictionary<string, string>((dark ? Phosphor : Daylight).Tokens)
		{
			[ThemeTokens.Accent] = accent,
			[ThemeTokens.Warn] = warn,
			[ThemeTokens.LinkMissing] = linkMissing,
			[ThemeVision.Key] = vision,
		};
		tokens.Remove(ThemeStyles.Texture);
		return new PortalTheme($"{vision}-{(dark ? "dark" : "light")}", name, dark, Published: true, tokens, BuiltIn: true);
	}

	private static PortalTheme Genre(string id, string name, bool dark, string[] colors, string display, string body,
		string corners, string texture, string ornament, string frame, string titles, string effect, string imagery,
		string? accent2 = null, string? accent3 = null)
	{
		var tokens = ThemeTokens.Editable.Zip(colors).ToDictionary(p => p.First, p => p.Second);
		if (accent2 is not null)
		{
			tokens[ThemeTokens.Accent2] = accent2;
		}

		if (accent3 is not null)
		{
			tokens[ThemeTokens.Accent3] = accent3;
		}

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
/// <param name="Parts">
/// The data attributes the portal puts on the page for the theme's look (<see cref="ThemeStyles.Parts"/>), by
/// name without <c>data-</c>.
/// </param>
/// <param name="Stylesheet">The theme's own stylesheet (<see cref="ThemeStylesheet"/>), or null; only a Custom theme's applies.</param>
public sealed record ResolvedTheme(
	string ThemeId,
	string Name,
	bool Dark,
	string Accent,
	string? RequestedAccent,
	bool AccentAdjusted,
	IReadOnlyDictionary<string, string> Tokens,
	IReadOnlyDictionary<string, string> Style,
	IReadOnlyDictionary<string, string> Parts,
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
	/// The theme the character sees: its chosen theme if that is still offered, else the built-in theme for its
	/// colour vision (<see cref="ThemeVision"/>), else the game's default, each for the browser's light or dark
	/// preference, else Phosphor.
	/// </summary>
	public static PortalTheme Pick(IReadOnlyList<PortalTheme> themes, PortalThemeDefaults defaults, bool prefersLight, string? chosenThemeId,
		string? vision = null)
		=> themes.FirstOrDefault(t => t.Id == chosenThemeId)
			?? themes.FirstOrDefault(t => t.Id == ThemeVision.ThemeId(vision, prefersLight))
			?? themes.FirstOrDefault(t => t.Id == defaults.For(prefersLight))
			?? themes.FirstOrDefault(t => t.Id == defaults.DarkThemeId)
			?? BuiltInThemes.Phosphor;

	/// <param name="vision">
	/// The reader's colour vision (<see cref="ThemeVision"/>), or null for the theme's own: the status and code colours
	/// are the ones that stay apart for it.
	/// </param>
	public static ResolvedTheme Resolve(PortalTheme theme, string? accent = null, string? vision = null)
	{
		var mode = ThemeStyles.ModeOf(theme.Tokens, theme.Stylesheet);
		var stylesheet = mode == ThemeStyles.Custom && !string.IsNullOrWhiteSpace(theme.Stylesheet) ? theme.Stylesheet : null;
		var (tokens, style) = Complete(ThemeStylesheet.WithColorOverrides(theme.Tokens, stylesheet));
		if (ThemeVision.Chosen(vision) is { } reader)
		{
			style[ThemeVision.Key] = reader;
		}

		var bg = ThemeColor.Parse(tokens[ThemeTokens.Background]);
		var surface = ThemeColor.Parse(tokens[ThemeTokens.Surface]);
		// A chosen accent is drawn on the page, cards, the current sidebar row and the sidebar itself.
		ThemeColor[] grounds = [bg, surface, ThemeColor.Parse(tokens[ThemeTokens.Surface2]), ThemeColor.Parse(tokens[ThemeTokens.Surface3])];

		var requested = ThemeColor.TryParse(accent, out var chosen) ? chosen : (ThemeColor?)null;
		var accentColor = requested is { } own
			? ReadableAgainst(own, theme.Dark, ThemeTokens.TextContrast, grounds)
			: ThemeColor.Parse(tokens[ThemeTokens.Accent]);
		tokens[ThemeTokens.Accent] = accentColor.Hex;

		Derive(tokens, theme.Dark, accentColor, ThemeVision.Of(style));

		return new ResolvedTheme(theme.Id, theme.Name, theme.Dark, accentColor.Hex, requested?.Hex,
			requested is { } asked && asked != accentColor, tokens, style, ThemeStyles.Parts(style, mode, theme.Dark), stylesheet);
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
				else if (ThemeTokens.Decorative.Contains(name))
				{
					if (!ThemeColor.TryParse(value, out _))
					{
						problems.Add($"Token '{name}' is not a #rrggbb colour: '{value}'.");
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
	/// The theme's colours, normalised to lower-case <c>#rrggbb</c> with Phosphor's for any it lacks (its decorative
	/// colours only where it sets them), and its Simple settings with the defaults for any it lacks, plus the parts
	/// and mode it sets itself.
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

		foreach (var name in ThemeTokens.Decorative)
		{
			if (tokens.TryGetValue(name, out var value) && ThemeColor.TryParse(value, out var color))
			{
				colors[name] = color.Hex;
			}
		}

		var style = ThemeStyles.Keys.ToDictionary(k => k,
			k => tokens.TryGetValue(k, out var value) && ThemeStyles.IsValid(k, value) ? value : ThemeStyles.Defaults[k], StringComparer.Ordinal);
		foreach (var key in ThemeStyles.Choices.Keys.Except(ThemeStyles.Keys))
		{
			if (tokens.TryGetValue(key, out var value) && ThemeStyles.IsValid(key, value))
			{
				style[key] = value;
			}
		}

		return (colors, style);
	}

	/// <summary>
	/// The status and kind colours every theme derives, with Phosphor's values (<c>tokens.css</c>). A theme or reader
	/// with another colour vision starts from <see cref="ThemeVision.StatusColors"/> instead.
	/// </summary>
	public static readonly IReadOnlyList<(string Name, string Hex)> StatusColors =
	[
		("danger", "#e57373"),
		("success", "#6cde9a"),
		("info", "#5aa9ff"),
		("special", "#b39cff"),
	];

	/// <summary>
	/// The syntax colours every theme derives, with Phosphor's values (<c>tokens.css</c>). A theme or reader with another
	/// colour vision starts from <see cref="ThemeVision.SyntaxColors"/> instead.
	/// </summary>
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

	/// <summary>
	/// The hues <c>tone()</c> names, with Phosphor's values, in the order of the standard ANSI colours they also stand for
	/// (<c>--ms-ansi-1</c> red to <c>--ms-ansi-6</c> cyan), orange and pink after. Every theme derives its own, readable on
	/// its surfaces. They are hues, so a reader's colour vision leaves them as they are, as it does <c>ansi()</c> colours.
	/// </summary>
	public static readonly IReadOnlyList<(string Name, string Hex)> HueColors =
	[
		("hue-red", "#f07178"),
		("hue-green", "#c3e88d"),
		("hue-yellow", "#ffcb6b"),
		("hue-blue", "#82aaff"),
		("hue-purple", "#c792ea"),
		("hue-cyan", "#89ddff"),
		("hue-orange", "#f78c6c"),
		("hue-pink", "#ff79c6"),
	];

	/// <summary>A colour's hue in degrees, whole, for the picture tones that tint toward the accent.</summary>
	private static int Hue(ThemeColor c)
	{
		// Compared as the byte channels, so no floating-point equality is involved.
		int r = c.R, g = c.G, b = c.B;
		int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
		if (d == 0) return 0;
		var h = max == r ? (double)(g - b) / d % 6 : max == g ? (double)(b - r) / d + 2 : (double)(r - g) / d + 4;
		return (int)Math.Round((h * 60 + 360) % 360);
	}

	/// <summary>The surface tinted with the accent, as far as <paramref name="text"/> still reads on it at 4.5:1.</summary>
	private static ThemeColor Highlight(ThemeColor surface, ThemeColor accent, ThemeColor text)
	{
		for (var share = 0.24; share > 0; share -= 0.02)
		{
			var tinted = surface.Mix(accent, share);
			if (ThemeColor.Contrast(text, tinted) >= ThemeTokens.TextContrast) return tinted;
		}
		return surface;
	}

	/// <summary><paramref name="color"/> taken toward <paramref name="page"/> until <paramref name="text"/> reads on it at 4.5:1.</summary>
	private static ThemeColor Behind(ThemeColor color, ThemeColor page, ThemeColor text)
	{
		for (var share = 0.0; share < 1; share += 0.05)
		{
			var behind = color.Mix(page, share);
			if (ThemeColor.Contrast(text, behind) >= ThemeTokens.TextContrast) return behind;
		}
		return page;
	}

	private static void Derive(Dictionary<string, string> tokens, bool dark, ThemeColor accent, string vision)
	{
		ThemeColor Get(string name) => ThemeColor.Parse(tokens[name]);
		var bg = Get(ThemeTokens.Background);
		var surface = Get(ThemeTokens.Surface);
		var surface3 = Get(ThemeTokens.Surface3);
		var rail = Get(ThemeTokens.Rail);
		var warn = Get(ThemeTokens.Warn);
		var textDim = Get(ThemeTokens.TextDim);

		tokens["accent-dim"] = accent.Mix(ThemeColor.Black, 0.22).Hex;
		tokens["accent-hue"] = Hue(accent).ToString(CultureInfo.InvariantCulture);
		tokens.TryAdd(ThemeTokens.Accent2, tokens[ThemeTokens.LinkMissing]);
		tokens.TryAdd(ThemeTokens.Accent3, tokens[ThemeTokens.Warn]);
		tokens["accent-on"] = OnAccent(accent).Hex;
		tokens["glow"] = accent.Triple;
		tokens["warn-tint"] = $"rgba({warn.Triple}, 0.14)";
		tokens["unread-alert"] = tokens[ThemeTokens.LinkMissing];
		tokens["mud-palette-background-gradient"] = $"radial-gradient(120% 100% at 80% -10%, {surface.Mix(bg, 0.1).Hex} 0%, {bg.Hex} 55%)";
		// The rail's icons: secondary text where it reads on the rail, else moved toward whichever of white or black the rail
		// sets off better. A light theme may have a dark or coloured rail, where its dark secondary text would vanish.
		tokens["rail-ink"] = ReadableAgainst(textDim, ThemeColor.Contrast(ThemeColor.White, rail) > ThemeColor.Contrast(ThemeColor.Black, rail), ThemeTokens.TextContrast, rail).Hex;
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
		foreach (var (name, hex) in ThemeVision.StatusColors(vision, dark))
		{
			tokens[name] = ReadableAgainst(ThemeColor.Parse(hex), dark, ThemeTokens.TextContrast, grounds).Hex;
		}

		// tone()'s tertiary colour, the theme's third decorative colour made readable, and its highlight, a background the
		// accent tints that text still reads on.
		tokens["tone-tertiary"] = ReadableAgainst(Get(ThemeTokens.Accent3), dark, ThemeTokens.TextContrast, grounds).Hex;
		tokens["highlight"] = Highlight(surface, accent, Get(ThemeTokens.Text)).Hex;

		// The hues tone() names, and the sixteen ANSI colours MarkupString's HTML reads (--ms-ansi-N), so ansi() colours
		// follow the theme as a terminal's colour scheme does. The normal colours are the hues; the bright ones are lighter
		// on a dark theme and darker on a light one. Behind text (--ms-ansi-bg-N) each is taken toward the page until the
		// theme's text reads on it. Black, white and the greys keep their usual values.
		var text = Get(ThemeTokens.Text);
		for (var i = 0; i < HueColors.Count; i++)
		{
			var (name, hex) = HueColors[i];
			var hue = ReadableAgainst(ThemeColor.Parse(hex), dark, ThemeTokens.TextContrast, grounds);
			tokens[name] = hue.Hex;
			if (i >= 6) continue;
			var bright = ReadableAgainst(hue.Mix(dark ? ThemeColor.White : ThemeColor.Black, dark ? 0.25 : 0.15), dark, ThemeTokens.TextContrast, grounds);
			tokens[$"ms-ansi-{i + 1}"] = hue.Hex;
			tokens[$"ms-ansi-{i + 9}"] = bright.Hex;
			tokens[$"ms-ansi-bg-{i + 1}"] = Behind(hue, bg, text).Hex;
			tokens[$"ms-ansi-bg-{i + 9}"] = Behind(bright, bg, text).Hex;
		}

		var codeBg = dark ? bg.Mix(ThemeColor.Black, 0.35) : surface3;
		tokens["code-bg"] = codeBg.Hex;
		tokens["code-text"] = ReadableAgainst(Get(ThemeTokens.Text), dark, ThemeTokens.TextContrast, codeBg).Hex;
		foreach (var (name, hex) in ThemeVision.SyntaxColors(vision, dark))
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
