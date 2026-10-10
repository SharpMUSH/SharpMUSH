using SharpMUSH.Library.API;
using SharpMUSH.Library.Models.Portal;
using ThemeColor = SharpMUSH.Library.Models.Portal.ThemeColor;

namespace SharpMUSH.Tests.Theme;

/// <summary>
/// A theme's own stylesheet: what saving refuses (anything that loads from another site, runs script or leaves its
/// style element), the colour tokens it sets counting for contrast, and the starter changing nothing.
/// </summary>
public class ThemeStylesheetTests
{
	public static IEnumerable<Func<string>> BuiltInIds() => BuiltInThemes.All.Select(t => (Func<string>)(() => t.Id));

	private static PortalTheme BuiltIn(string id) => BuiltInThemes.All.Single(t => t.Id == id);

	[Test]
	[Arguments(".kit-card.kit-card { border-radius: 0; }")]
	[Arguments(":root { --bg: #101010; --radius-card: 4px; }")]
	[Arguments(".phosphor-shell { background-image: url(/uploads/paper.png); }")]
	[Arguments(".phosphor-shell { background-image: url('/uploads/paper.png'); }")]
	[Arguments(".x { background: url(\"data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg'/%3E\"); }")]
	[Arguments(".x { background: url(data:image/png;base64,iVBORw0KGgo=); }")]
	[Arguments(".x { filter: url(#grain); }")]
	[Arguments(".x::before { content: \"\\2726\"; }")]
	[Arguments("@media (max-width: 760px) { .x { display: none; } }")]
	[Arguments("/* a comment with url(http://example.com) in it */ .x { color: red; }")]
	[Arguments(":ROOT { --text: #fff; --text-dim: #CCCCCC !important }")]
	[Arguments(":root { --te\\78t: #ffffff; }")]
	[Arguments("")]
	public async Task OrdinaryCssIsAccepted(string css)
		=> await Assert.That(ThemeStylesheet.Validate(css)).IsEmpty();

	[Test]
	[Arguments("@import url(/other.css);", "@import")]
	[Arguments("@\\69mport '/other.css';", "@import")]
	[Arguments("@IMPORT '/other.css';", "@import")]
	[Arguments(".x { background: url(https://example.com/a.png); }", "loads from elsewhere")]
	[Arguments(".x { background: url(//example.com/a.png); }", "loads from elsewhere")]
	[Arguments(".x { background: \\75 rl(https://example.com/a.png); }", "loads from elsewhere")]
	[Arguments(".x { background: url(data:text/html,hi); }", "loads from elsewhere")]
	[Arguments(".x { background: image-set(\"https://example.com/a.png\" 1x); }", "image-set")]
	[Arguments("@font-face { font-family: X; src: url(https://fonts.example.com/x.woff2); }", "loads from elsewhere")]
	[Arguments("</style><script>alert(1)</script>", "'<'")]
	[Arguments(".x { content: \"\\3c/style>\"; }", "'<'")]
	[Arguments(".x { width: expression(alert(1)); }", "expression()")]
	[Arguments(".x { background: url(javascript:alert(1)); }", "script")]
	[Arguments(".x { -moz-binding: url(/x.xml#y); }", "-moz-binding")]
	[Arguments(".x { behavior: url(/x.htc); }", "behavior")]
	[Arguments(".x { color: red; ", "Braces")]
	[Arguments(".x { color: red; } }", "Braces")]
	[Arguments(".x { color: red; } /* not closed", "comment")]
	[Arguments(".x { background: url(/a.png; }", "not closed")]
	[Arguments(":root { --text: rgb(255 255 255); }", "--text")]
	[Arguments(":root { --text: white; }", "--text")]
	[Arguments("html { --bg: #000000; }", "--bg")]
	[Arguments(":root, .x { --bg: #000000; }", "--bg")]
	[Arguments(":root:lang(zh) { --accent: #ff0000; }", "--accent")]
	[Arguments(":root { --text: #ffffff; } body { --text: #000000; }", "--text")]
	public async Task AnythingThatLoadsFromElsewhereOrEscapesItsStyleIsRefused(string css, string because)
	{
		var problems = ThemeStylesheet.Validate(css);

		await Assert.That(problems).IsNotEmpty();
		await Assert.That(problems.Any(p => p.Contains(because, StringComparison.OrdinalIgnoreCase))).IsTrue()
			.Because($"expected a problem naming '{because}', got: {string.Join(" | ", problems)}");
	}

	[Test]
	public async Task ATooLongStylesheetIsRefused()
		=> await Assert.That(ThemeStylesheet.Validate(new string(' ', ThemeStylesheet.MaxLength + 1))).IsNotEmpty();

	[Test]
	public async Task ColourTokensSetInRootCountAsTheThemesOwn()
	{
		const string css = """
			:root { --text: #FFF; --bg: #000000 !important; --radius: 2px; --not-a-token: #123456 }
			.x { --surface: #ff0000; }
			/* :root { --accent: #00ff00; } */
			""";

		var overrides = ThemeStylesheet.ColorOverrides(css);

		await Assert.That(overrides).IsEquivalentTo(new Dictionary<string, string> { ["text"] = "#ffffff", ["bg"] = "#000000" });
	}

	[Test]
	public async Task AnUpperCaseRootAndAnEscapedNameAreReadAsTheBrowserReadsThem()
	{
		await Assert.That(ThemeStylesheet.ColorOverrides(":ROOT { --text: #fff; }")).ContainsKey("text");
		await Assert.That(ThemeStylesheet.ColorOverrides(":root { --te\\78t: #ffffff; }")).ContainsKey("text");
	}

	[Test]
	public async Task AStylesheetsColoursAreWhatTheContrastReportAndTheResolvedThemeUse()
	{
		var theme = BuiltInThemes.Daylight with { Id = "pale", BuiltIn = false, Stylesheet = ":root { --text-faint: #eeeeee; }" };

		var effective = ThemeStylesheet.WithColorOverrides(theme.Tokens, theme.Stylesheet);
		var low = ThemeResolver.Contrasts(effective).Where(c => c.Ratio < ThemeTokens.TextContrast).Select(c => c.Foreground).Distinct();
		var resolved = ThemeResolver.Resolve(theme);

		await Assert.That(low).IsEquivalentTo(new[] { ThemeTokens.TextFaint });
		await Assert.That(resolved.Token(ThemeTokens.TextFaint)).IsEqualTo("#eeeeee");
	}

	[Test]
	public async Task TheStylesheetFollowsTheTokensAndACharactersAccentFollowsTheStylesheet()
	{
		var theme = BuiltInThemes.Phosphor with { Id = "own", BuiltIn = false, Stylesheet = ":root { --accent: #ff0000; } .kit-card.kit-card { border-radius: 0; }" };

		var css = ThemeResolver.Resolve(theme, "#5aa9ff").Css;
		var plain = ThemeResolver.Resolve(theme).Css;

		await Assert.That(css.IndexOf(".kit-card.kit-card", StringComparison.Ordinal)).IsGreaterThan(css.IndexOf("--bg:", StringComparison.Ordinal));
		await Assert.That(css.LastIndexOf("--accent:#5aa9ff;", StringComparison.Ordinal))
			.IsGreaterThan(css.IndexOf(".kit-card.kit-card", StringComparison.Ordinal))
			.Because("a character's accent is set again after the stylesheet");
		await Assert.That(plain.TrimEnd()).EndsWith("border-radius: 0; }")
			.Because("with no accent of the character's own, the stylesheet's accent stands");
	}

	[Test]
	public async Task AThemeWithoutAStylesheetResolvesAsBefore()
		=> await Assert.That(ThemeResolver.Resolve(BuiltInThemes.Phosphor).Css).DoesNotContain("\n");

	[Test]
	[MethodDataSource(nameof(BuiltInIds))]
	public async Task TheStarterIsAcceptedAndChangesNothing(string id)
	{
		var theme = BuiltIn(id);
		var starter = ThemeStylesheet.Starter(ThemeResolver.Resolve(theme));

		await Assert.That(ThemeStylesheet.Validate(starter)).IsEmpty();
		await Assert.That(ThemeStylesheet.ColorOverrides(starter)).IsEmpty();
		await Assert.That(starter.Length).IsLessThan(ThemeStylesheet.MaxLength);

		// With the comments taken out, only the :root rule (empty) and the hooks' empty rules are left.
		var bare = System.Text.RegularExpressions.Regex.Replace(starter, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);
		await Assert.That(System.Text.RegularExpressions.Regex.IsMatch(bare, @"[a-z-]+\s*:\s*[^{};]+;")).IsFalse()
			.Because("every declaration in the starter is commented out");
		foreach (var hook in ThemeStylesheet.Hooks)
		{
			await Assert.That(starter).Contains($"{hook.Selector} {{");
		}
	}

	[Test]
	public async Task TheStarterListsEveryColourTokenWithTheThemesValue()
	{
		var resolved = ThemeResolver.Resolve(BuiltInThemes.Horror);
		var starter = ThemeStylesheet.Starter(resolved);

		foreach (var token in ThemeTokens.Editable)
		{
			await Assert.That(starter).Contains($"--{token}: {resolved.Token(token)};");
		}
	}

	private const string PartsCss = """
		/* A drawing: [data-texture="stars"] in a comment is not a rule. */
		[data-texture="stars"] { --texture-mask-text: url(/themes/drawings/shared/starfield-200.svg) 0 0 / 200px 200px; --texture-size: auto; }
		[data-scheme="dark"][data-texture="stars"] { --texture-ink-text: color-mix(in srgb, var(--text) 40%, transparent); }
		[data-scheme="light"][data-texture="stars"] { --texture-ink-text: var(--text); }
		[data-frame-marks="bar"], [data-frame-marks="tape"] { --card-marks: linear-gradient(var(--accent), var(--accent)); }
		[data-texture="grid"] { --texture: none; }
		.theme-texture { position: fixed; }
		""";

	[Test]
	public async Task PartRulesAreTheRulesAThemesPartsApplyAsRootRules()
	{
		var parts = new Dictionary<string, string> { ["texture"] = "stars", ["scheme"] = "dark", ["frame-marks"] = "tape" };

		var rules = ThemeStylesheet.PartRules(PartsCss, parts);

		await Assert.That(rules).IsEquivalentTo((string[])
		[
			":root {\n\t--texture-mask-text: url(/themes/drawings/shared/starfield-200.svg) 0 0 / 200px 200px;\n\t--texture-size: auto;\n}",
			":root {\n\t--texture-ink-text: color-mix(in srgb, var(--text) 40%, transparent);\n}",
			":root {\n\t--card-marks: linear-gradient(var(--accent), var(--accent));\n}",
		], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task TheStarterListsThePartRulesCommentedOut()
	{
		var resolved = ThemeResolver.Resolve(BuiltInThemes.Phosphor with
		{
			Tokens = new Dictionary<string, string>(BuiltInThemes.Phosphor.Tokens) { [ThemeStyles.Texture] = "stars" },
		});

		var starter = ThemeStylesheet.Starter(resolved, PartsCss);

		await Assert.That(starter).Contains("/* :root {\n\t--texture-mask-text: url(/themes/drawings/shared/starfield-200.svg)");
		await Assert.That(starter).Contains("/* :root {\n\t--texture-ink-text: color-mix(in srgb, var(--text) 40%, transparent);\n} */");
		await Assert.That(starter).DoesNotContain("--texture: none");
		await Assert.That(ThemeStylesheet.Validate(starter)).IsEmpty();
		await Assert.That(starter).IsNotEqualTo(ThemeStylesheet.Starter(resolved));
	}

	[Test]
	[MethodDataSource(nameof(BuiltInIds))]
	public async Task EveryThemeDerivesReadableStatusAndSyntaxColours(string id)
	{
		var theme = ThemeResolver.Resolve(BuiltIn(id));
		ThemeColor Token(string name) => ThemeColor.Parse(theme.Token(name));

		foreach (var (name, _) in ThemeResolver.StatusColors)
		{
			foreach (var ground in (string[])[ThemeTokens.Background, ThemeTokens.Surface, ThemeTokens.Surface3])
			{
				await Assert.That(ThemeColor.Contrast(Token(name), Token(ground))).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast)
					.Because($"{id}: {name} on {ground}");
			}
		}

		foreach (var name in ThemeResolver.SyntaxColors.Select(s => s.Name).Append("code-text"))
		{
			await Assert.That(ThemeColor.Contrast(Token(name), Token("code-bg"))).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast)
				.Because($"{id}: {name} on code-bg");
		}
	}

	[Test]
	[MethodDataSource(nameof(BuiltInIds))]
	public async Task EveryThemeDerivesReadableToneColours(string id)
	{
		var theme = ThemeResolver.Resolve(BuiltIn(id));
		ThemeColor Token(string name) => ThemeColor.Parse(theme.Token(name));
		string[] grounds = [ThemeTokens.Background, ThemeTokens.Surface, ThemeTokens.Surface3];

		foreach (var name in ThemeResolver.HueColors.Select(h => h.Name).Append("tone-tertiary")
			.Concat(Enumerable.Range(1, 6).Concat(Enumerable.Range(9, 6)).Select(slot => $"ms-ansi-{slot}")))
		{
			foreach (var ground in grounds)
			{
				await Assert.That(ThemeColor.Contrast(Token(name), Token(ground))).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast)
					.Because($"{id}: {name} on {ground}");
			}
		}

		// Marked text and ansi() backgrounds keep the theme's text readable on them.
		foreach (var name in Enumerable.Range(1, 6).Concat(Enumerable.Range(9, 6)).Select(slot => $"ms-ansi-bg-{slot}").Append("highlight"))
		{
			await Assert.That(ThemeColor.Contrast(Token(ThemeTokens.Text), Token(name))).IsGreaterThanOrEqualTo(ThemeTokens.TextContrast)
				.Because($"{id}: text on {name}");
		}
	}

	[Test]
	public async Task PhosphorDerivesTheStatusAndSyntaxValuesTokensCssShips()
	{
		var theme = ThemeResolver.Resolve(BuiltInThemes.Phosphor);

		foreach (var (name, hex) in ThemeResolver.StatusColors.Concat(ThemeResolver.SyntaxColors).Concat(ThemeResolver.HueColors))
		{
			await Assert.That(theme.Token(name)).IsEqualTo(hex).Because(name);
		}

		await Assert.That(theme.Token("code-bg")).IsEqualTo("#090a0b");
	}
}
