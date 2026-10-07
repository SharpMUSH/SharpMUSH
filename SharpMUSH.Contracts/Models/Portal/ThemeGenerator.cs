using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// Portal theme colours from a MarkupString <see cref="ThemePalette"/>: the same palettes the game's layout themes
/// use, whether generated from one colour (<see cref="ThemePalette.Generate"/>), a preset
/// (<see cref="ThemePalette.Presets"/>, including the eight genres and the base16 schemes), or palette JSON
/// (<see cref="ThemePalette.TryParse(string, out ThemePalette?, out string?)"/>). Every colour that carries text comes
/// out readable against the surfaces it is drawn on (<see cref="ThemeTokens.ContrastPairs"/>).
/// </summary>
public static class ThemeGenerator
{
	/// <summary>
	/// The palette as the portal's <see cref="ThemeTokens.Editable"/> colours, and whether they make a dark theme.
	/// The palette's background is the page; its surface and foreground set the shades between. Its primary is the
	/// accent, its warning the warning colour and its error the colour of missing links.
	/// </summary>
	public static (Dictionary<string, string> Colors, bool Dark) FromPalette(ThemePalette palette)
	{
		ArgumentNullException.ThrowIfNull(palette);
		var dark = palette.Mode == ThemeMode.Dark;
		var paletteBg = Color(palette.BackgroundColor);
		var fg = palette[ThemeRole.Foreground] is { } foreground ? Color(foreground.Resolved) : dark ? ThemeColor.White : ThemeColor.Black;
		var paletteSurface = palette[ThemeRole.Surface] is { } s ? Color(s.Resolved) : paletteBg.Mix(fg, 0.06);

		// Cards sit a step lighter than the page in a dark theme; in a light one they are the palette's own
		// background and the page a step darker, as Daylight's are.
		var (bg, surface) = dark
			? (paletteBg, paletteSurface.Mix(paletteBg, 0.4))
			: (paletteBg.Mix(paletteSurface, 0.5), paletteBg);
		var surface2 = surface.Mix(fg, 0.04);
		var surface3 = dark ? bg.Mix(ThemeColor.Black, 0.15) : bg.Mix(ThemeColor.Black, 0.03);
		var rail = dark ? bg.Mix(ThemeColor.Black, 0.3) : bg.Mix(ThemeColor.Black, 0.06);
		ThemeColor[] grounds = [bg, surface, surface2, surface3];

		ThemeColor Readable(ThemeColor color) => ThemeResolver.ReadableAgainst(color, dark, ThemeTokens.TextContrast, grounds);
		ThemeColor Role(ThemeRole role, ThemeColor fallback) => palette[role] is { } c ? Color(c.Resolved) : fallback;

		var text = Readable(fg);
		var colors = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[ThemeTokens.Background] = bg.Hex,
			[ThemeTokens.Surface] = surface.Hex,
			[ThemeTokens.Surface2] = surface2.Hex,
			[ThemeTokens.Surface3] = surface3.Hex,
			[ThemeTokens.Rail] = rail.Hex,
			[ThemeTokens.Text] = text.Hex,
			[ThemeTokens.TextDim] = Readable(text.Mix(bg, 0.3)).Hex,
			[ThemeTokens.TextFaint] = Readable(text.Mix(bg, 0.45)).Hex,
			[ThemeTokens.Border] = bg.Mix(text, 0.14).Hex,
			[ThemeTokens.BorderSoft] = bg.Mix(text, 0.08).Hex,
			[ThemeTokens.Accent] = Readable(Role(ThemeRole.Primary, ThemeColor.Parse("#00f5b7"))).Hex,
			[ThemeTokens.Warn] = Readable(Role(ThemeRole.Warning, ThemeColor.Parse("#ffb454"))).Hex,
			[ThemeTokens.LinkMissing] = Readable(Role(ThemeRole.Error, ThemeColor.Parse("#ff8a8a"))).Hex,
		};
		return (colors, dark);
	}

	/// <summary>
	/// <see cref="FromPalette"/> of <see cref="ThemePalette.Generate"/>: colours made from <paramref name="seed"/>.
	/// </summary>
	/// <param name="seed">A <c>#rrggbb</c> colour.</param>
	/// <param name="harmony">How the palette's other hues are picked from the seed's.</param>
	/// <param name="dark">A dark theme, or a light one.</param>
	/// <param name="contrast">0 to 1: how far above the minimum contrast the colours go.</param>
	public static Dictionary<string, string> Generate(string seed, ThemeHarmony harmony, bool dark, double contrast)
	{
		var color = ThemeColor.Parse(seed);
		var palette = ThemePalette.Generate(new RgbColor(color.R, color.G, color.B), harmony,
			dark ? ThemeMode.Dark : ThemeMode.Light, contrast);
		return FromPalette(palette).Colors;
	}

	/// <summary>
	/// The palette named in <paramref name="text"/> (a preset) or written there as palette JSON, as portal colours; or
	/// why it cannot be read. A generated palette (a genre, or one with a <c>seed</c>) is made again in
	/// <paramref name="dark"/>'s mode when that is given; a fixed scheme keeps its own.
	/// </summary>
	public static Result<(Dictionary<string, string> Colors, bool Dark)> FromText(string text, bool? dark = null)
	{
		if (!ThemePalette.TryParse(text, out var palette, out var error) || palette is null)
		{
			return new Error<string>(error ?? "not a palette");
		}

		if (dark is { } wanted && palette.Seed is not null)
		{
			palette = palette.InMode(wanted ? ThemeMode.Dark : ThemeMode.Light);
		}

		return FromPalette(palette);
	}

	private static ThemeColor Color(RgbColor rgb) => new(rgb.R, rgb.G, rgb.B);
}
