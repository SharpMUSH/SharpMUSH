using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using ThemeColor = SharpMUSH.Library.Models.Portal.ThemeColor;

namespace SharpMUSH.Tests.Theme;

/// <summary>
/// The colour vision themes: one dark and one light per vision, picked for a player who names a vision and no theme,
/// and colours that stay apart when seen with that vision.
/// </summary>
public class ThemeVisionTests
{
	/// <summary>
	/// Far enough apart in OKLab to tell at a glance: about six times the smallest difference people notice (~0.02).
	/// Phosphor's info and special colours are 0.007 apart for a protan reader.
	/// </summary>
	private const double Apart = 0.12;

	/// <summary>Code colours are many, and read beside the code they colour, so they need less.</summary>
	private const double CodeApart = 0.08;

	public static IEnumerable<Func<string>> Visions() => ThemeVision.All.Where(v => v != ThemeVision.Typical).Select(v => (Func<string>)(() => v));

	public static IEnumerable<Func<string>> VisionThemeIds()
		=> BuiltInThemes.All.Where(t => ThemeVision.Of(t.Tokens) != ThemeVision.Typical).Select(t => (Func<string>)(() => t.Id));

	private static PortalTheme BuiltIn(string id) => BuiltInThemes.All.Single(t => t.Id == id);

	[Test]
	[MethodDataSource(nameof(Visions))]
	public async Task EveryVisionHasADarkAndALightTheme(string vision)
	{
		var dark = BuiltIn($"{vision}-dark");
		var light = BuiltIn($"{vision}-light");

		await Assert.That(dark.Dark).IsTrue();
		await Assert.That(light.Dark).IsFalse();
		await Assert.That(ThemeVision.Of(dark.Tokens)).IsEqualTo(vision);
		await Assert.That(ThemeVision.Of(light.Tokens)).IsEqualTo(vision);
		await Assert.That(ThemeVision.ThemeId(vision, light: false)).IsEqualTo(dark.Id);
		await Assert.That(ThemeVision.ThemeId(vision, light: true)).IsEqualTo(light.Id);
	}

	[Test]
	public async Task AVisionPicksItsThemeOnlyWhenNoThemeIsChosen()
	{
		var themes = BuiltInThemes.All;
		var defaults = new PortalThemeDefaults(BuiltInThemes.PhosphorId, BuiltInThemes.DaylightId);

		await Assert.That(ThemeResolver.Pick(themes, defaults, prefersLight: false, null, ThemeVision.Deutan).Id).IsEqualTo("deutan-dark");
		await Assert.That(ThemeResolver.Pick(themes, defaults, prefersLight: true, null, ThemeVision.Deutan).Id).IsEqualTo("deutan-light");
		await Assert.That(ThemeResolver.Pick(themes, defaults, prefersLight: true, "fantasy", ThemeVision.Deutan).Id).IsEqualTo("fantasy");
		await Assert.That(ThemeResolver.Pick(themes, defaults, prefersLight: true, null, ThemeVision.Typical).Id).IsEqualTo(BuiltInThemes.DaylightId);
		await Assert.That(ThemeResolver.Pick(themes, defaults, prefersLight: false, null, "unheard-of").Id).IsEqualTo(BuiltInThemes.PhosphorId);
	}

	[Test]
	public async Task AReadersVisionSwapsTheStatusAndCodeColoursOfAnyTheme()
	{
		var typical = ThemeResolver.Resolve(BuiltInThemes.Fantasy);
		var deutan = ThemeResolver.Resolve(BuiltInThemes.Fantasy, vision: ThemeVision.Deutan);
		var ownTheme = ThemeResolver.Resolve(BuiltInThemes.DeutanLight);

		await Assert.That(deutan.Token("success")).IsNotEqualTo(typical.Token("success"));
		await Assert.That(deutan.Token("syntax-danger")).IsNotEqualTo(typical.Token("syntax-danger"));
		await Assert.That(deutan.Style[ThemeVision.Key]).IsEqualTo(ThemeVision.Deutan);
		// The theme's own colours stay; only what the theme derives changes.
		await Assert.That(deutan.Token(ThemeTokens.Accent)).IsEqualTo(typical.Token(ThemeTokens.Accent));
		await Assert.That(ownTheme.Token("success")).IsEqualTo(ThemeResolver.Resolve(BuiltInThemes.Daylight, vision: ThemeVision.Deutan).Token("success"));
	}

	[Test]
	[MethodDataSource(nameof(VisionThemeIds))]
	public async Task AVisionThemesColoursStayApartForThatVision(string id)
	{
		var theme = ThemeResolver.Resolve(BuiltIn(id));
		var vision = ThemeVision.Of(theme.Style);
		foreach (var (a, b) in Pairs(vision))
		{
			await Assert.That(Seen.Difference(theme.Token(a), theme.Token(b), vision)).IsGreaterThanOrEqualTo(Apart)
				.Because($"{id}: {a} and {b} as a {vision} reader sees them");
		}

		if (vision == ThemeVision.Mono) return;

		var code = ThemeResolver.SyntaxColors.Select(s => s.Name).ToList();
		foreach (var (a, b) in code.SelectMany((a, i) => code.Skip(i + 1).Select(b => (a, b))))
		{
			await Assert.That(Seen.Difference(theme.Token(a), theme.Token(b), vision)).IsGreaterThanOrEqualTo(CodeApart)
				.Because($"{id}: {a} and {b} as a {vision} reader sees them");
		}
	}

	[Test]
	public async Task TheTypicalColoursRunTogetherForAProtanReader()
	{
		// What the vision themes are for: the typical set does not pass the check above.
		var phosphor = ThemeResolver.Resolve(BuiltInThemes.Phosphor);

		await Assert.That(Seen.Difference(phosphor.Token("info"), phosphor.Token("special"), ThemeVision.Protan)).IsLessThan(Apart);
		await Assert.That(Seen.Difference(phosphor.Token(ThemeTokens.Accent), phosphor.Token(ThemeTokens.LinkMissing), ThemeVision.Deutan)).IsLessThan(Apart);
	}

	/// <summary>
	/// The colours that mean different things side by side. Danger and a missing link both mean "something is wrong",
	/// so they may match. Lightness alone cannot set seven colours apart above the contrast floor, so for
	/// <see cref="ThemeVision.Mono"/> only the pairs that are read against each other count.
	/// </summary>
	private static IEnumerable<(string, string)> Pairs(string vision)
	{
		if (vision == ThemeVision.Mono)
		{
			return
			[
				("danger", "success"), (ThemeTokens.Accent, ThemeTokens.LinkMissing), ("danger", ThemeTokens.Warn),
				(ThemeTokens.Accent, ThemeTokens.Warn), ("info", "special"),
			];
		}

		string[] colours = [ThemeTokens.Accent, "success", "danger", ThemeTokens.Warn, ThemeTokens.LinkMissing, "info", "special"];
		return colours.SelectMany((a, i) => colours.Skip(i + 1).Select(b => (a, b))).Where(p => p is not ("danger", ThemeTokens.LinkMissing));
	}

	/// <summary>
	/// How colours look to a reader with each vision: Machado, Oliveira and Fernandes (2009) at full severity for the
	/// dichromacies, luminance alone for monochromacy, compared in OKLab.
	/// </summary>
	private static class Seen
	{
		private static readonly Dictionary<string, double[,]> Simulation = new()
		{
			[ThemeVision.Protan] = new[,] { { 0.152286, 1.052583, -0.204868 }, { 0.114503, 0.786281, 0.099216 }, { -0.003882, -0.048116, 1.051998 } },
			[ThemeVision.Deutan] = new[,] { { 0.367322, 0.860646, -0.227968 }, { 0.280085, 0.672501, 0.047413 }, { -0.011820, 0.042940, 0.968881 } },
			[ThemeVision.Tritan] = new[,] { { 1.255528, -0.076749, -0.178779 }, { -0.078411, 0.930809, 0.147602 }, { 0.004733, 0.691367, 0.303900 } },
		};

		public static double Difference(string a, string b, string vision)
		{
			var (l1, a1, b1) = OkLab(As(vision, ThemeColor.Parse(a)));
			var (l2, a2, b2) = OkLab(As(vision, ThemeColor.Parse(b)));
			return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
		}

		private static double[] As(string vision, ThemeColor color)
		{
			double[] linear = [Linear(color.R), Linear(color.G), Linear(color.B)];
			if (vision == ThemeVision.Mono)
			{
				var y = 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
				return [y, y, y];
			}

			var m = Simulation[vision];
			return [.. Enumerable.Range(0, 3).Select(i => Math.Clamp(m[i, 0] * linear[0] + m[i, 1] * linear[1] + m[i, 2] * linear[2], 0, 1))];
		}

		private static double Linear(byte channel)
		{
			var c = channel / 255.0;
			return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
		}

		private static (double L, double A, double B) OkLab(double[] rgb)
		{
			var l = Math.Cbrt(0.4122214708 * rgb[0] + 0.5363325363 * rgb[1] + 0.0514459929 * rgb[2]);
			var m = Math.Cbrt(0.2119034982 * rgb[0] + 0.6806995451 * rgb[1] + 0.1073969566 * rgb[2]);
			var s = Math.Cbrt(0.0883024619 * rgb[0] + 0.2817188376 * rgb[1] + 0.6299787005 * rgb[2]);
			return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
				1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
				0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
		}
	}
}
