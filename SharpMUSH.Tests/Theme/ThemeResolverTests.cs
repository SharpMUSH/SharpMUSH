using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using ThemeColor = SharpMUSH.Library.Models.Portal.ThemeColor;

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
	public async Task RailIconsReadOnEveryBuiltInThemesRail()
	{
		foreach (var builtIn in BuiltInThemes.All)
		{
			var theme = ThemeResolver.Resolve(builtIn);
			await Assert.That(ThemeColor.Contrast(ThemeColor.Parse(theme.Token("rail-ink")), ThemeColor.Parse(theme.Token(ThemeTokens.Rail))))
				.IsGreaterThanOrEqualTo(ThemeTokens.TextContrast).Because(builtIn.Name);
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
		var genres = new[] { "fantasy", "historical", "horror", "modern", "mystery", "romance", "science-fiction", "spiritual" };

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
	public async Task PhosphorsPartsAreTheDefaultsTokensCssShips()
	{
		var theme = ThemeResolver.Resolve(BuiltInThemes.Phosphor);

		await Assert.That(theme.Parts[ThemeStyles.FontDisplay]).IsEqualTo("ui");
		await Assert.That(theme.Parts[ThemeStyles.Texture]).IsEqualTo("none");
		await Assert.That(theme.Parts[ThemeStyles.FrameMarks]).IsEqualTo("none");
		await Assert.That(theme.Parts[ThemeStyles.FrameEdge]).IsEqualTo("line");
		await Assert.That(theme.Parts[ThemeStyles.OrnamentGlyphs]).IsEqualTo("none");
		await Assert.That(theme.Parts[ThemeStyles.EffectColor]).IsEqualTo("text");
		await Assert.That(theme.Parts[ThemeStyles.Imagery]).IsEqualTo("natural");
		await Assert.That(theme.Parts[ThemeStyles.Scheme]).IsEqualTo("dark");
	}

	[Test]
	public async Task EachGenreHasATextureAndFrameOfItsOwn()
	{
		var genres = BuiltInThemes.All.Skip(2).Select(t => ThemeResolver.Complete(t.Tokens).Style).ToList();

		await Assert.That(genres.Select(s => s[ThemeStyles.Texture]).Distinct().Count()).IsEqualTo(genres.Count);
		await Assert.That(genres.Select(s => s[ThemeStyles.Frame]).Distinct().Count()).IsEqualTo(genres.Count);
		await Assert.That(genres.Select(s => s[ThemeStyles.FontDisplay]).Distinct().Count()).IsEqualTo(genres.Count);
	}

	[Test]
	public async Task AThemesCssIsColoursOnlyAndItsLookIsItsParts()
	{
		foreach (var theme in BuiltInThemes.All)
		{
			var resolved = ThemeResolver.Resolve(theme);
			await Assert.That(resolved.Css).DoesNotContain("url(").Because(theme.Id);
			await Assert.That(resolved.Css).DoesNotContain("--texture").Because(theme.Id);
			await Assert.That(resolved.Parts.Keys).IsEquivalentTo(ThemeStyles.PartKeys.Append(ThemeStyles.Scheme)).Because(theme.Id);
		}
	}

	[Test]
	public async Task PicturesAreTintedTowardTheAccentInUse()
	{
		// #e0453a is hue 4, #00c8ff hue 193; imagery.css turns sepia (near 38) toward it.
		await Assert.That(ThemeResolver.Resolve(BuiltInThemes.Horror).Token("accent-hue")).IsEqualTo("4");
		await Assert.That(ThemeResolver.Resolve(BuiltInThemes.Horror, "#00c8ff").Token("accent-hue")).IsEqualTo("193");
		await Assert.That(ThemeResolver.Resolve(BuiltInThemes.Mystery).Parts[ThemeStyles.Imagery]).IsEqualTo("noir");
	}

	[Test]
	public async Task AGenreThemeCarriesItsEmbellishments()
	{
		var scifi = ThemeResolver.Resolve(BuiltInThemes.ScienceFiction).Parts;
		var fantasy = ThemeResolver.Resolve(BuiltInThemes.Fantasy).Parts;

		await Assert.That(scifi[ThemeStyles.FontDisplay]).IsEqualTo("orbitron");
		await Assert.That(scifi[ThemeStyles.Titles]).IsEqualTo("caps");
		await Assert.That(scifi[ThemeStyles.OrnamentGlyphs]).IsEqualTo("brackets");
		await Assert.That(scifi[ThemeStyles.Texture]).IsEqualTo("scanlines");
		await Assert.That(scifi[ThemeStyles.FrameMarks]).IsEqualTo("corners");
		await Assert.That(fantasy[ThemeStyles.FontBody]).IsEqualTo("serif");
		await Assert.That(fantasy[ThemeStyles.OrnamentUnderline]).IsEqualTo("fade");
		await Assert.That(fantasy[ThemeStyles.EffectColor]).IsEqualTo("accent");
	}

	[Test]
	public async Task ASimpleThemeTakesThePartsItsSettingsBringAndIgnoresItsOwn()
	{
		var tokens = BuiltInThemes.Fantasy.Tokens.ToDictionary();
		tokens[ThemeStyles.FrameMarks] = "plating";

		var parts = ThemeStyles.PartsOf(tokens, ThemeStyles.Simple);

		await Assert.That(parts[ThemeStyles.FrameMarks]).IsEqualTo("filigree");
		await Assert.That(ThemeStyles.ModeOf(tokens, null)).IsEqualTo(ThemeStyles.Simple);
	}

	[Test]
	public async Task AComplexThemeMixesPartsFromDifferentSettings()
	{
		var tokens = BuiltInThemes.Fantasy.Tokens.ToDictionary();
		tokens[ThemeStyles.Mode] = ThemeStyles.Complex;
		tokens[ThemeStyles.FrameMarks] = "plating";
		tokens[ThemeStyles.EffectShadow] = "neon-does-not-exist";

		var parts = ThemeResolver.Resolve(BuiltInThemes.Fantasy with { Tokens = tokens }).Parts;

		await Assert.That(parts[ThemeStyles.FrameMarks]).IsEqualTo("plating");
		// The rest of the filigree frame, and the gilt effect's shadow where the theme's own is not a choice.
		await Assert.That(parts[ThemeStyles.FrameEdge]).IsEqualTo("line");
		await Assert.That(parts[ThemeStyles.EffectShadow]).IsEqualTo("gilt");
		await Assert.That(ThemeResolver.Validate(tokens)).Contains(p => p.Contains("effect-shadow", StringComparison.Ordinal));
	}

	[Test]
	public async Task AStylesheetCountsOnlyInCustomMode()
	{
		const string sheet = ":root { --radius: 0px; }";
		var custom = BuiltInThemes.Phosphor with { Tokens = new Dictionary<string, string>(BuiltInThemes.Phosphor.Tokens) { [ThemeStyles.Mode] = ThemeStyles.Custom }, Stylesheet = sheet };
		var complex = BuiltInThemes.Phosphor with { Tokens = new Dictionary<string, string>(BuiltInThemes.Phosphor.Tokens) { [ThemeStyles.Mode] = ThemeStyles.Complex }, Stylesheet = sheet };
		var unmarked = BuiltInThemes.Phosphor with { Stylesheet = sheet };

		await Assert.That(ThemeResolver.Resolve(custom).Stylesheet).IsEqualTo(sheet);
		await Assert.That(ThemeResolver.Resolve(complex).Stylesheet).IsNull();
		await Assert.That(ThemeStyles.ModeOf(unmarked.Tokens, unmarked.Stylesheet)).IsEqualTo(ThemeStyles.Custom);
		await Assert.That(ThemeResolver.Resolve(unmarked).Stylesheet).IsEqualTo(sheet);
	}

	[Test]
	public async Task ADecorativeColourIsTheThemesOwnOrDerived()
	{
		await Assert.That(ThemeResolver.Resolve(BuiltInThemes.RealRobot).Token(ThemeTokens.Accent2)).IsEqualTo("#d6202a");
		var phosphor = ThemeResolver.Resolve(BuiltInThemes.Phosphor);
		await Assert.That(phosphor.Token(ThemeTokens.Accent2)).IsEqualTo(phosphor.Token(ThemeTokens.LinkMissing));
		await Assert.That(phosphor.Token(ThemeTokens.Accent3)).IsEqualTo(phosphor.Token(ThemeTokens.Warn));
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
	public async Task AStyleValueThatIsNotAChoiceNeverReachesThePage()
	{
		var theme = BuiltInThemes.Phosphor with { Tokens = new Dictionary<string, string>(BuiltInThemes.Phosphor.Tokens) { [ThemeStyles.Texture] = "url(evil)" } };
		var resolved = ThemeResolver.Resolve(theme);

		await Assert.That(resolved.Css).DoesNotContain("evil");
		await Assert.That(resolved.Parts[ThemeStyles.Texture]).IsEqualTo("none");
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
