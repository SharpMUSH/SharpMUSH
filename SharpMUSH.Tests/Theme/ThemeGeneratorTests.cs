using MarkupString;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.Theme;

/// <summary>Portal themes made from MarkupString palettes: every one a valid theme that passes the contrast report.</summary>
public class ThemeGeneratorTests
{
	public static IEnumerable<Func<(string Preset, bool Dark)>> Presets()
		=> ThemePalette.Presets.SelectMany(p => new[] { true, false }.Select(dark => (Func<(string, bool)>)(() => (p.Name, dark))));

	public static IEnumerable<Func<(ThemeHarmony Harmony, bool Dark, double Contrast)>> Generated()
		=> from harmony in Enum.GetValues<ThemeHarmony>()
			 from dark in new[] { true, false }
			 from contrast in new[] { 0.0, 0.5, 1.0 }
			 select (Func<(ThemeHarmony, bool, double)>)(() => (harmony, dark, contrast));

	private static async Task PassesTheContrastReport(Dictionary<string, string> colors, string because)
	{
		await Assert.That(ThemeResolver.Validate(colors)).IsEmpty().Because(because);
		foreach (var (fg, bg, ratio) in ThemeResolver.Contrasts(colors))
		{
			await Assert.That(ratio).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast).Because($"{because}: {fg} on {bg}");
		}
	}

	[Test]
	[MethodDataSource(nameof(Presets))]
	public async Task EveryPresetMakesAReadableTheme(string preset, bool dark)
	{
		var made = ThemeGenerator.FromText(preset, dark).Expect<(Dictionary<string, string> Colors, bool Dark)>();

		await PassesTheContrastReport(made.Colors, $"{preset} ({(dark ? "dark" : "light")})");
	}

	[Test]
	[MethodDataSource(nameof(Generated))]
	public async Task EverySeededPaletteMakesAReadableTheme(ThemeHarmony harmony, bool dark, double contrast)
	{
		foreach (var seed in (string[])["#6b4fa8", "#00f5b7", "#b01e28", "#808080", "#ffff00", "#000000"])
		{
			await PassesTheContrastReport(ThemeGenerator.Generate(seed, harmony, dark, contrast), $"{seed} {harmony} {dark} {contrast}");
		}
	}

	[Test]
	public async Task AGenrePaletteIsMadeAgainForTheThemesModeButABase16SchemeKeepsItsOwn()
	{
		var fantasyLight = ThemeGenerator.FromText("fantasy", dark: false).Expect<(Dictionary<string, string> Colors, bool Dark)>();
		var nordLight = ThemeGenerator.FromText("nord", dark: false).Expect<(Dictionary<string, string> Colors, bool Dark)>();

		await Assert.That(fantasyLight.Dark).IsFalse();
		await Assert.That(nordLight.Dark).IsTrue().Because("nord's colours are dark whatever mode is asked for");
	}

	[Test]
	public async Task PaletteJsonIsReadAndItsColoursUsed()
	{
		var made = ThemeGenerator.FromText("""{"preset":"nord","colors":{"primary":"#ff00ff"}}""")
			.Expect<(Dictionary<string, string> Colors, bool Dark)>();
		var accent = Library.Models.Portal.ThemeColor.Parse(made.Colors[ThemeTokens.Accent]);

		await Assert.That(accent.R).IsEqualTo(accent.B).Because("the primary set in JSON is the accent, kept magenta");
		await Assert.That(accent.G).IsLessThan(accent.R);
	}

	[Test]
	[Arguments("no-such-palette")]
	[Arguments("""{"seed":"blue"}""")]
	public async Task AnUnreadablePaletteSaysWhy(string text)
	{
		var error = ThemeGenerator.FromText(text).Expect<Error<string>>();

		await Assert.That(error.Value).IsNotEmpty();
	}
}
