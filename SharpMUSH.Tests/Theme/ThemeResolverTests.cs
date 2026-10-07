using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;

namespace SharpMUSH.Tests.Theme;

/// <summary>The colour rules behind every theme: what a theme must keep readable, and how a character's accent is fitted to it.</summary>
public class ThemeResolverTests
{
	public static IEnumerable<Func<string>> BuiltInIds() => BuiltInThemes.All.Select(t => (Func<string>)(() => t.Id));

	private static PortalTheme BuiltIn(string id) => BuiltInThemes.All.Single(t => t.Id == id);

	[Test]
	[MethodDataSource(nameof(BuiltInIds))]
	public async Task EveryBuiltInThemeReachesAAOnEveryPair(string id)
	{
		foreach (var (fg, bg, ratio) in ThemeResolver.Contrasts(BuiltIn(id).Tokens))
		{
			await Assert.That(ratio).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast).Because($"{id}: {fg} on {bg}");
		}
	}

	[Test]
	[MethodDataSource(nameof(BuiltInIds))]
	public async Task EveryBuiltInThemeSetsEveryEditableToken(string id)
		=> await Assert.That(ThemeResolver.Validate(BuiltIn(id).Tokens)).IsEmpty();

	[Test]
	public async Task PhosphorResolvesToTheValuesTokensCssShips()
	{
		var theme = ThemeResolver.Resolve(BuiltInThemes.Phosphor);

		await Assert.That(theme.Token("accent")).IsEqualTo("#00f5b7");
		await Assert.That(theme.Token("glow")).IsEqualTo("0,245,183");
		await Assert.That(theme.AccentAdjusted).IsFalse();
		await Assert.That(theme.Css).StartsWith(":root{color-scheme:dark;");
		await Assert.That(theme.Css).Contains("--bg:#0e0f11;");
	}

	[Test]
	[Arguments("#ffb454")]
	[Arguments("#a6e22e")]
	[Arguments("#00f5b7")]
	[Arguments("#ffffff")]
	public async Task ACharacterAccentTooFaintOnALightThemeIsDarkenedUntilItReads(string accent)
	{
		var theme = ThemeResolver.Resolve(BuiltInThemes.Daylight, accent);
		var shown = ThemeColor.Parse(theme.Accent);

		await Assert.That(theme.AccentAdjusted).IsTrue();
		await Assert.That(theme.RequestedAccent).IsEqualTo(accent);
		await Assert.That(ThemeColor.Contrast(shown, ThemeColor.Parse(theme.Token("bg")))).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast);
		await Assert.That(ThemeColor.Contrast(shown, ThemeColor.Parse(theme.Token("surface")))).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast);
		await Assert.That(shown.Luminance).IsLessThan(ThemeColor.Parse(accent).Luminance);
	}

	[Test]
	[Arguments("#1f2a6b")]
	[Arguments("#000000")]
	[Arguments("#7a0019")]
	public async Task ACharacterAccentTooDarkOnADarkThemeIsLightenedUntilItReads(string accent)
	{
		var theme = ThemeResolver.Resolve(BuiltInThemes.Phosphor, accent);
		var shown = ThemeColor.Parse(theme.Accent);

		await Assert.That(theme.AccentAdjusted).IsTrue();
		await Assert.That(ThemeColor.Contrast(shown, ThemeColor.Parse(theme.Token("surface")))).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast);
		await Assert.That(shown.Luminance).IsGreaterThan(ThemeColor.Parse(accent).Luminance);
	}

	[Test]
	public async Task AReadableAccentIsUsedAsChosen()
	{
		var theme = ThemeResolver.Resolve(BuiltInThemes.Phosphor, "#5aa9ff");

		await Assert.That(theme.Accent).IsEqualTo("#5aa9ff");
		await Assert.That(theme.AccentAdjusted).IsFalse();
		await Assert.That(theme.Token("glow")).IsEqualTo("90,169,255");
	}

	[Test]
	public async Task EverySwatchReadsOnEveryBuiltInThemeOnceFitted()
	{
		foreach (var builtIn in BuiltInThemes.All)
		{
			foreach (var (name, hex) in BuiltInThemes.AccentSwatches)
			{
				var theme = ThemeResolver.Resolve(builtIn, hex);
				var accent = ThemeColor.Parse(theme.Accent);
				await Assert.That(ThemeColor.Contrast(accent, ThemeColor.Parse(theme.Token("surface"))))
					.IsGreaterThanOrEqualTo(ThemeTokens.TextContrast).Because($"{name} on {builtIn.Name}");
				await Assert.That(ThemeColor.Contrast(ThemeColor.Parse(theme.Token("accent-on")), accent))
					.IsGreaterThanOrEqualTo(ThemeTokens.TextContrast).Because($"text on a {name} button on {builtIn.Name}");
			}
		}
	}

	[Test]
	public async Task AMalformedAccentLeavesTheThemesOwn()
	{
		var theme = ThemeResolver.Resolve(BuiltInThemes.Daylight, "not a colour");

		await Assert.That(theme.Accent).IsEqualTo(BuiltInThemes.Daylight.Tokens["accent"]);
		await Assert.That(theme.RequestedAccent).IsNull();
	}

	[Test]
	public async Task ALightThemeGetsALightOocBand()
	{
		var dark = ThemeResolver.Resolve(BuiltInThemes.Phosphor);
		var light = ThemeResolver.Resolve(BuiltInThemes.Daylight);

		await Assert.That(ThemeColor.Parse(light.Token("ooc-band-bg")).Luminance).IsGreaterThan(0.5);
		await Assert.That(ThemeColor.Contrast(ThemeColor.Parse(light.Token("text")), ThemeColor.Parse(light.Token("ooc-band-bg"))))
			.IsGreaterThanOrEqualTo(ThemeTokens.TextContrast);
		await Assert.That(ThemeColor.Contrast(ThemeColor.Parse(dark.Token("text")), ThemeColor.Parse(dark.Token("ooc-band-bg"))))
			.IsGreaterThanOrEqualTo(ThemeTokens.TextContrast);
		await Assert.That(light.Css).StartsWith(":root{color-scheme:light;");
	}

	[Test]
	public async Task PickFallsBackFromAMissingThemeToTheDefaultForTheBrowsersPreferenceThenPhosphor()
	{
		IReadOnlyList<PortalTheme> themes = BuiltInThemes.All;
		var defaults = new PortalThemeDefaults("horror", "romance");

		await Assert.That(ThemeResolver.Pick(themes, defaults, prefersLight: false, "gone").Id).IsEqualTo("horror");
		await Assert.That(ThemeResolver.Pick(themes, defaults, prefersLight: true, null).Id).IsEqualTo("romance");
		await Assert.That(ThemeResolver.Pick(themes, defaults, prefersLight: true, "mystery").Id).IsEqualTo("mystery");
		await Assert.That(ThemeResolver.Pick(themes, new PortalThemeDefaults("gone", "gone"), prefersLight: true, null).Id).IsEqualTo("phosphor");
	}

	[Test]
	public async Task ThereIsAThemeForEveryMsspGenre()
	{
		var genres = new[] { "adult", "fantasy", "historical", "horror", "modern", "mystery", "romance", "science-fiction", "spiritual" };

		foreach (var genre in genres)
		{
			var theme = BuiltInThemes.All.SingleOrDefault(t => t.Id == genre);
			await Assert.That(theme).IsNotNull().Because(genre);
			await Assert.That(ThemeStyles.Keys.All(k => theme!.Tokens.ContainsKey(k))).IsTrue().Because($"{genre} sets every style");
		}
	}

	[Test]
	public async Task NoTwoBuiltInThemesLookAlike()
	{
		var looks = BuiltInThemes.All.Select(t => string.Join(",", ThemeStyles.Keys.Select(k => ThemeResolver.Complete(t.Tokens).Style[k]))).ToList();
		var genreLooks = looks.Skip(2).ToList();

		await Assert.That(genreLooks.Distinct().Count()).IsEqualTo(genreLooks.Count);
	}

	[Test]
	public async Task PhosphorsStyleIsWhatTokensCssShips()
	{
		var theme = ThemeResolver.Resolve(BuiltInThemes.Phosphor);

		await Assert.That(theme.Token("font-display")).IsEqualTo("'Hanken Grotesk', system-ui, sans-serif");
		await Assert.That(theme.Token("radius")).IsEqualTo("9px");
		await Assert.That(theme.Token("radius-card")).IsEqualTo("16px");
		await Assert.That(theme.Token("texture")).IsEqualTo("none");
		await Assert.That(theme.Token("ornament-before")).IsEqualTo("none");
		await Assert.That(theme.Token("card-bg")).IsEqualTo("var(--surface)");
		await Assert.That(theme.Token("title-underline-pad")).IsEqualTo("0px");
	}

	[Test]
	public async Task AGenreThemeCarriesItsEmbellishments()
	{
		var scifi = ThemeResolver.Resolve(BuiltInThemes.ScienceFiction);
		var fantasy = ThemeResolver.Resolve(BuiltInThemes.Fantasy);

		await Assert.That(scifi.Token("font-display")).StartsWith("'Orbitron'");
		await Assert.That(scifi.Token("title-transform")).IsEqualTo("uppercase");
		await Assert.That(scifi.Token("ornament-before")).IsEqualTo("\"\\5B\"");
		await Assert.That(scifi.Token("texture")).Contains("repeating-linear-gradient");
		await Assert.That(scifi.Token("card-bg")).Contains("no-repeat").And.EndsWith("var(--surface)");
		await Assert.That(fantasy.Token("texture")).Contains("feTurbulence");
		await Assert.That(fantasy.Token("font-ui")).StartsWith("Georgia");
	}

	[Test]
	public async Task AStyleSettingIsOneOfItsChoicesAndMayBeLeftOut()
	{
		var tokens = BuiltInThemes.Phosphor.Tokens.ToDictionary();
		await Assert.That(ThemeResolver.Validate(tokens)).IsEmpty();

		tokens[ThemeStyles.Texture] = "url(evil)";
		await Assert.That(string.Join("\n", ThemeResolver.Validate(tokens))).Contains("Style 'texture' is one of none, grain");
	}

	[Test]
	public async Task AStyleValueThatIsNotAChoiceNeverReachesTheStylesheet()
	{
		var theme = BuiltInThemes.Phosphor with { Tokens = new Dictionary<string, string>(BuiltInThemes.Phosphor.Tokens) { [ThemeStyles.Texture] = "url(evil)" } };

		await Assert.That(ThemeResolver.Resolve(theme).Css).DoesNotContain("evil");
	}

	[Test]
	public async Task ValidateNamesEveryMissingMalformedOrUnknownToken()
	{
		var tokens = BuiltInThemes.Phosphor.Tokens.ToDictionary();
		tokens.Remove("bg");
		tokens["text"] = "red";
		tokens["sparkle"] = "#ffffff";

		var problems = string.Join("\n", ThemeResolver.Validate(tokens));

		await Assert.That(problems).Contains("'bg' is missing");
		await Assert.That(problems).Contains("'text' is not a #rrggbb colour");
		await Assert.That(problems).Contains("'sparkle' is not a theme token");
	}

	[Test]
	[Arguments("#fff", "#ffffff")]
	[Arguments("00F5B7", "#00f5b7")]
	[Arguments(" #0E0F11 ", "#0e0f11")]
	public async Task ColoursParseInTheUsualHexForms(string text, string hex)
	{
		await Assert.That(ThemeColor.TryParse(text, out var color)).IsTrue();
		await Assert.That(color.Hex).IsEqualTo(hex);
	}

	[Test]
	[Arguments("")]
	[Arguments("#12345")]
	[Arguments("rgb(0,0,0)")]
	[Arguments("#gggggg")]
	public async Task OtherColourFormsAreRefused(string text)
		=> await Assert.That(ThemeColor.TryParse(text, out _)).IsFalse();

	[Test]
	public async Task ContrastMatchesWcag()
	{
		await Assert.That(ThemeColor.Contrast(ThemeColor.Black, ThemeColor.White)).IsEqualTo(21.0).Within(0.01);
		await Assert.That(ThemeColor.Contrast(ThemeColor.Parse("#777777"), ThemeColor.White)).IsEqualTo(4.48).Within(0.01);
	}
}
