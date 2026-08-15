using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// Fixes the boundary between shell CSS and page CSS. The shell describes the device and owns
/// every viewport @media; pages and components describe themselves and query the .phosphor-page
/// container. The split is not stylistic: the content column's width depends on a collapsible
/// sidebar and two admin-configured widget asides, so a media query inside a page is guessing.
/// </summary>
public class ResponsiveConventionsTests
{
	private static IEnumerable<string> ScopedStylesheets() =>
		Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor.css", SearchOption.AllDirectories);

	/// <summary>
	/// Scoped stylesheets belonging to a routable page — the ones the tier rules are about. A
	/// component's stylesheet is excluded: it sizes to its own box wherever it is placed, and may
	/// legitimately carry no page tier at all.
	/// </summary>
	private static IEnumerable<string> PageStylesheets() =>
		Directory.EnumerateFiles(Path.Join(ClientSource.RazorRoot, "Pages"), "*.razor.css", SearchOption.AllDirectories)
			.Where(f =>
			{
				var razor = f[..^".css".Length];
				return File.Exists(razor) && Regex.IsMatch(File.ReadAllText(razor), @"^[ \t]*@page\b", RegexOptions.Multiline);
			});

	/// <summary>
	/// Scoped stylesheets paired with the <c>.razor</c> whose markup they are scoped to. The pair is
	/// the unit both reachability rules need: what a scoped selector can match is decided by what
	/// that one file's markup declares, not by anything in the stylesheet alone.
	/// </summary>
	private static IEnumerable<(string Css, string Razor)> ScopedPairs() =>
		ScopedStylesheets()
			.Select(css => (Css: css, Razor: css[..^".css".Length]))
			.Where(pair => File.Exists(pair.Razor));

	private static string Rel(string path) =>
		Path.GetRelativePath(ClientSource.RazorRoot, path).Replace('\\', '/');

	/// <summary>
	/// Every rule below matches on CSS syntax, and this codebase documents its stylesheets heavily —
	/// including prose that names the very at-rules being banned. Scanning raw text would fail a file
	/// for explaining the convention it follows.
	///
	/// A regex cannot do this correctly: a non-greedy <c>/\*.*?\*/</c> opens on a literal <c>/*</c>
	/// inside a quoted string value (e.g. <c>content: "/* not a comment";</c>) and does not close
	/// until the *next* genuine <c>*/</c> anywhere later in the file, silently deleting everything
	/// between — including a live @media rule. This walks the text once, tracking whether it is
	/// inside a <c>"</c>/<c>'</c> string, so a <c>/*</c> there is just two characters. An unterminated
	/// comment strips to end of file rather than throwing.
	/// </summary>
	private static string StripComments(string css)
	{
		var result = new StringBuilder(css.Length);
		var quote = '\0';
		var i = 0;

		while (i < css.Length)
		{
			var c = css[i];

			if (quote != '\0')
			{
				result.Append(c);
				// An escaped quote (or escaped anything) inside a string does not end the string,
				// so consume the pair together rather than re-examining the escaped character.
				if (c == '\\' && i + 1 < css.Length)
				{
					result.Append(css[i + 1]);
					i += 2;
					continue;
				}
				if (c == quote)
					quote = '\0';
				i++;
				continue;
			}

			if (c is '"' or '\'')
			{
				quote = c;
				result.Append(c);
				i++;
				continue;
			}

			if (c == '/' && i + 1 < css.Length && css[i + 1] == '*')
			{
				var end = css.IndexOf("*/", i + 2, StringComparison.Ordinal);
				i = end < 0 ? css.Length : end + 2;
				continue;
			}

			result.Append(c);
			i++;
		}

		return result.ToString();
	}

	/// <summary>
	/// Routable pages that legitimately ship no stylesheet: they redirect, or render only
	/// components that carry their own.
	/// </summary>
	private static readonly HashSet<string> PagesWithoutStylesheetByDesign = new(StringComparer.Ordinal)
	{
		"Pages/Admin/BannedNamesRedirect.razor",
		"Pages/Admin/RestrictionsRedirect.razor",
		"Pages/Admin/SitelockRedirect.razor",
		"Pages/NotFound.razor",
		"Pages/SettingsCharactersRedirect.razor",
		"Pages/WikiIndex.razor",
	};

	private static readonly string[] SanctionedTiers = ["48rem", "64rem", "90rem"];

	/// <summary>
	/// Page stylesheets that declare no container tier, each with the reason it needs none. The
	/// spec's verification item #5 requires every page to either state its tiers or be named here:
	/// a page with neither is responsive by accident, and nobody notices which of the two it is.
	/// </summary>
	private static readonly Dictionary<string, string> PagesWithoutContainerTiersByDesign = new(StringComparer.Ordinal)
	{
		["Pages/Admin/AdminMedia.razor.css"] =
			"the asset grid is a repeat(auto-fit, minmax(...)) grid on a child component; it re-flows "
			+ "by track count, so a tier would only restate what auto-fit already does",
		["Pages/Admin/AdminWiki.razor.css"] =
			"same — its stat grid is repeat(auto-fit, minmax(180px, 1fr)), which self-tiers",
		["Pages/Register.razor.css"] =
			"the route renders a <PageTitle> and redirects to /login?tab=register; there is no layout here",
		["Pages/WikiPage.razor.css"] =
			"renders only <WikiView Mode=\"View\">, whose own stylesheet carries the tiers",
		["Pages/WikiPageEdit.razor.css"] =
			"renders only <WikiView Mode=\"Edit\"> behind a height:100% wrapper; WikiEdit carries the tiers",
	};

	/// <summary>
	/// Narrow and medium are downgrades applied as the container shrinks, so they gate on
	/// <c>max-width</c>. Roomy is an upgrade applied as the container grows, so it gates on
	/// <c>min-width</c> — the spec's tier table defines it that way explicitly. A <c>max-width:
	/// 90rem</c> (or wider) tier is not just off-spec: the shell caps <c>.phosphor-page</c> at
	/// <c>--content-max</c> (1400px) above a 1601px viewport, so a threshold above 1400px is
	/// either unreachable at ordinary viewports or — when the sidebar is collapsed instead of
	/// expanded — flickers as the container crosses the 1400px cap and the threshold in the same
	/// narrow window (verified live: widening from 1600px to 1601px snaps a `max-width: 90rem`
	/// tier from unstacked to stacked, because the container drops from 1538px straight to the
	/// 1400px cap). This shipped once already, silently, because the literal-only regex above
	/// accepts either direction for any sanctioned value.
	/// </summary>
	private static readonly Dictionary<string, string> RequiredTierDirection = new(StringComparer.Ordinal)
	{
		["48rem"] = "max",
		["64rem"] = "max",
		["90rem"] = "min",
	};

	[Test]
	public async Task PagesQueryTheirContainerRatherThanTheViewport()
	{
		var offenders = ScopedStylesheets()
			.Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"@media\b"))
			.Select(Rel)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a page cannot see the sidebar or the admin-configured widget asides, so a "
				+ "viewport query inside one is wrong whenever either is present; use @container page");
	}

	[Test]
	public async Task StripCommentsDoesNotOpenACommentOnASlashStarInsideAStringLiteral()
	{
		// Regression case for a reviewer-found defect: a `/*` inside a quoted CSS string value is not
		// a comment opener, and a stripper that treats it as one deletes everything up to the *next*
		// genuine `*/` in the file — including a live @media rule between the two.
		const string css = """
			.x {
				content: "/* fake-opener, not a comment";
			}
			@media (max-width: 760px) {
				.x { color: red; }
			}
			/* genuine-comment */
			""";

		var stripped = StripComments(css);

		await Assert.That(stripped).Contains("@media (max-width: 760px)")
			.Because("a `/*` opening inside a string literal must not swallow real CSS that follows it");
		await Assert.That(stripped).DoesNotContain("genuine-comment")
			.Because("a genuine comment outside any string must still be stripped");
	}

	[Test]
	public async Task StripCommentsHandlesAnUnterminatedComment()
	{
		const string css = ".x { color: red; } /* never closed";

		var stripped = StripComments(css);

		await Assert.That(stripped).Contains(".x { color: red; }");
		await Assert.That(stripped).DoesNotContain("never closed")
			.Because("an unterminated comment strips to end of file rather than being left in place");
	}

	[Test]
	public async Task TheShellDoesNotQueryContainers()
	{
		var shell = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));

		await Assert.That(Regex.IsMatch(StripComments(shell), @"@container\b")).IsFalse()
			.Because("the shell decides how much width content gets; it never asks");
	}

	[Test]
	public async Task ViewportQueriesLiveOnlyInTheShellOrTheEscapeHatches()
	{
		// globals.css is the second permitted home, and only because of what it holds: overrides for
		// elements MudBlazor renders into its body-level portal, outside .phosphor-page. Those have no
		// container ancestor to query, so the viewport is the only width available to them.
		var permitted = new[] { "shell.css", "globals.css" };

		var offenders = Directory.EnumerateFiles(ClientSource.CssRoot, "*.css")
			.Where(f => !permitted.Contains(Path.GetFileName(f)))
			.Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"@media\b"))
			.Select(Path.GetFileName)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("device-facing rules belong to the shell; the only exception is content that "
				+ "renders outside the container and so cannot query it");
	}

	/// <summary>
	/// Matches only the header of a *named* <c>@container page (...)</c> rule — from the name up
	/// to (not including) the block's opening brace — so a query against an unnamed component
	/// container (<c>@container (max-width: 30rem) { … }</c>) never enters the header group at
	/// all. The three sanctioned literals and their directions describe <c>.phosphor-page</c>'s
	/// bounded width, shaped by the sidebar and asides and capped at 1400px; a component's own
	/// box has no relationship to those numbers; it may be 200px in an aside or full-width, so it
	/// picks values from its own content instead. The header is captured whole, rather than
	/// matching each <c>(min|max)-width: …)</c> directly off <c>@container</c>, so a compound
	/// condition — <c>@container page (min-width: 90rem) and (max-width: 120rem)</c> — still
	/// yields every width test inside it, not just the first.
	/// </summary>
	private static IEnumerable<Match> NamedPageContainerConditions(string css) =>
		NamedPageContainerBlocks(css)
			.SelectMany(header => Regex.Matches(header, @"(?<dir>min|max)-width:\s*(?<value>[^)]+)\)"));

	/// <summary>
	/// The same block headers, in source order, one entry per <c>@container page</c> rule — the
	/// unit the cascade actually resolves, which the flattened condition list above cannot express.
	/// </summary>
	private static IEnumerable<string> NamedPageContainerBlocks(string css) =>
		Regex.Matches(css, @"@container\s+page\s*(?<header>\([^{]*)\{")
			.Select(block => block.Groups["header"].Value);

	[Test]
	public async Task ContainerTiersUseOnlyTheSanctionedLiterals()
	{
		var offenders = new List<string>();

		foreach (var file in ScopedStylesheets())
		{
			foreach (var m in NamedPageContainerConditions(StripComments(File.ReadAllText(file))))
			{
				var value = m.Groups["value"].Value.Trim();
				if (!SanctionedTiers.Contains(value))
					offenders.Add($"{Rel(file)}: {value}");
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because($"page tiers drift into a private set of breakpoints unless they are fixed at {string.Join(" / ", SanctionedTiers)}; "
				+ "a component querying its own unnamed container is unconstrained by this rule");
	}

	[Test]
	public async Task SanctionedTiersUseTheCorrectDirection()
	{
		var offenders = new List<string>();

		foreach (var file in ScopedStylesheets())
		{
			foreach (var m in NamedPageContainerConditions(StripComments(File.ReadAllText(file))))
			{
				var value = m.Groups["value"].Value.Trim();
				var dir = m.Groups["dir"].Value;
				if (RequiredTierDirection.TryGetValue(value, out var required) && dir != required)
					offenders.Add($"{Rel(file)}: {dir}-width: {value} (must be {required}-width)");
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because("narrow/medium gate on max-width (a downgrade as the container shrinks) and "
				+ "roomy gates on min-width (an upgrade as the container grows); a max-width tier at "
				+ "90rem or wider sits above the shell's 1400px content cap, so it is unreachable or "
				+ "flicker-prone rather than merely off-spec. This is a page-tier rule; a component's "
				+ "own container has no relationship to the shell's content cap");
	}

	[Test]
	public async Task UnnamedComponentContainerQueriesAreUnconstrained()
	{
		// A component declares its own container and queries it unnamed, choosing values from its
		// own content — it may be 200px in an aside or full-width on the home page, so it has no
		// relationship to the page-tier literals or their directions. Neither guard should so much
		// as look at this rule.
		const string css = """
			.widget-root { container-type: inline-size; }
			@container (max-width: 30rem) {
				.widget-root { flex-direction: column; }
			}
			@container (min-width: 55rem) {
				.widget-root { flex-direction: row; }
			}
			""";

		var stripped = StripComments(css);

		await Assert.That(NamedPageContainerConditions(stripped)).IsEmpty()
			.Because("an unnamed @container query is a component sizing itself, not a page tier, "
				+ "and must not be matched by the page-tier literal/direction extraction at all");
	}

	[Test]
	public async Task NoScopedStylesheetPositionsAnythingFixed()
	{
		// container-type: inline-size makes .phosphor-page a containing block for fixed descendants,
		// so a fixed element inside page content would anchor to the content column rather than the
		// viewport. Fixed chrome belongs to the shell.
		var offenders = ScopedStylesheets()
			.Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"position:\s*fixed"))
			.Select(Rel)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a query container is a containing block for position:fixed; put fixed chrome in shell.css");
	}

	[Test]
	public async Task MigratedStylesheetsCarryNoImportantDeclarations()
	{
		// Vendor CSS sits in the `vendor` cascade layer and the scoped bundle is unlayered, so the
		// scoped rule already wins on layer order regardless of specificity. Every !important here
		// was simulating that by hand.
		var offenders = ScopedStylesheets()
			.Where(f => StripComments(File.ReadAllText(f)).Contains("!important", StringComparison.Ordinal))
			.Select(Rel)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("unlayered scoped CSS already beats the vendor layer; !important hides real conflicts");
	}

	[Test]
	public async Task GlobalStylesheetsCarryNoImportantDeclarations()
	{
		// The scoped-CSS rule below is only half the story, and the weaker half. Global sheets are
		// imported into cascade layers (custom.css) while the scoped bundle is unlayered, and an
		// !important in a *layered* rule beats an unlayered normal declaration whatever its
		// specificity — so an !important here silently outranks every page stylesheet in the app,
		// which is the inversion this whole boundary exists to delete. Layer order already puts
		// these sheets above `vendor`, so the flag buys nothing and costs that.
		//
		// monaco-overrides.css is unlayered rather than exempt: Monaco injects its own unlayered
		// stylesheet at runtime, which no layered rule can outrank, so those overrides win on
		// specificity from outside the layers instead of by force. That is the sanctioned answer
		// when a vendor ships CSS we cannot import — never !important.
		var offenders = Directory.EnumerateFiles(ClientSource.CssRoot, "*.css")
			.Where(f => StripComments(File.ReadAllText(f)).Contains("!important", StringComparison.Ordinal))
			.Select(Path.GetFileName)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a layered !important outranks every unlayered page stylesheet; layer order "
				+ "already beats vendor CSS, and an unlayered override beats what layers cannot reach");
	}

	[Test]
	public async Task MultiTierStylesheetsStateTheirTiersRoomyThenMediumThenNarrow()
	{
		// Both max-width tiers match below 48rem, so the narrow block only wins by being authored
		// last. A file that states narrow before medium disables narrow outright, and nothing about
		// the rendered page says so — it just quietly gets the medium layout at phone width. The
		// spec fixes the order at roomy (min-width) → medium (max-width: 64rem) → narrow
		// (max-width: 48rem) so the cascade is uniform across every stylesheet, not merely correct
		// in the files someone happened to check.
		var offenders = new List<string>();

		foreach (var file in ScopedStylesheets())
		{
			var tiers = NamedPageContainerBlocks(StripComments(File.ReadAllText(file)))
				.Select(header => Regex.Match(header, @"(?<dir>min|max)-width:\s*(?<value>[\d.]+)rem"))
				.Where(m => m.Success)
				.Select(m => (Dir: m.Groups["dir"].Value, Value: decimal.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture)))
				.ToList();

			var sawMax = false;
			var previousMax = decimal.MaxValue;

			foreach (var (dir, value) in tiers)
			{
				if (dir == "min")
				{
					if (sawMax)
						offenders.Add($"{Rel(file)}: min-width: {value}rem comes after a max-width tier");
					continue;
				}

				sawMax = true;
				if (value > previousMax)
					offenders.Add($"{Rel(file)}: max-width: {value}rem comes after max-width: {previousMax}rem (must descend)");
				previousMax = value;
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because("a narrower max-width tier authored before a wider one is dead below the "
				+ "narrower threshold, because the wider block matches there too and wins on order");
	}

	[Test]
	public async Task EveryPageStylesheetDeclaresAContainerTierOrIsExempt()
	{
		// Verification item #5 of the design spec, and the one that was never built. A page with no
		// tier is not necessarily wrong — an auto-fit grid re-flows on its own, and a page that
		// renders one child component delegates to that component's stylesheet — but "right by
		// design" and "nobody ever looked" are indistinguishable without a list that says which.
		var offenders = PageStylesheets()
			.Where(f => !Regex.IsMatch(StripComments(File.ReadAllText(f)), @"@container\b"))
			.Select(Rel)
			.Where(r => !PagesWithoutContainerTiersByDesign.ContainsKey(r))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a page that states no tier is either delegating or unconsidered, and only an "
				+ "explicit exemption with a reason tells the next reader which one it is");
	}

	[Test]
	public async Task TheContainerTierExemptionListHasNoStaleEntries()
	{
		// Same rot as the stylesheet exemption below it: once an exempt page grows a tier, the
		// entry stops describing reality and starts hiding the page from the check above.
		var stale = PagesWithoutContainerTiersByDesign.Keys
			.Where(r =>
			{
				var path = Path.Join(ClientSource.RazorRoot, r);
				return File.Exists(path) && Regex.IsMatch(StripComments(File.ReadAllText(path)), @"@container\b");
			})
			.Order(StringComparer.Ordinal)
			.ToList();

		var missing = PagesWithoutContainerTiersByDesign.Keys
			.Where(r => !File.Exists(Path.Join(ClientSource.RazorRoot, r)))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(stale).IsEmpty()
			.Because("a page that now declares a tier must be checked by "
				+ "EveryPageStylesheetDeclaresAContainerTierOrIsExempt, not exempted from it");
		await Assert.That(missing).IsEmpty()
			.Because("an exemption naming a file that no longer exists documents nothing");
	}

	[Test]
	public async Task EveryRoutablePageHasAStylesheet()
	{
		var offenders = Directory.EnumerateFiles(Path.Join(ClientSource.RazorRoot, "Pages"), "*.razor", SearchOption.AllDirectories)
			.Where(f => Regex.IsMatch(File.ReadAllText(f), @"^[ \t]*@page\b", RegexOptions.Multiline))
			.Where(f => !File.Exists(f + ".css"))
			.Select(Rel)
			.Where(r => !PagesWithoutStylesheetByDesign.Contains(r))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a page with no stylesheet has nowhere to declare its container tiers, which is "
				+ "how a page ends up responsive by accident rather than by design");
	}

	[Test]
	public async Task TheJavaScriptBreakpointMirrorMatchesTheShell()
	{
		// layout.js decides whether the hamburger opens the drawer or collapses the rail, so it has
		// to agree with the media condition that decides which of those is on screen.
		var js = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "js", "layout.js"));
		var shell = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));

		var condition = Regex.Match(js, @"matchMedia\(\s*'(?<q>[^']+)'\s*\)");
		await Assert.That(condition.Success).IsTrue().Because("layout.js must state the condition it mirrors");

		var normalised = Regex.Replace(condition.Groups["q"].Value, @"\s+", " ").Trim();
		var shellNormalised = Regex.Replace(shell, @"\s+", " ");

		await Assert.That(shellNormalised).Contains($"@media {normalised}")
			.Because($"layout.js mirrors '{normalised}', which must appear verbatim in shell.css");
	}

	[Test]
	public async Task TheStylesheetExemptionListHasNoStaleEntries()
	{
		// The moment a page named here gains a .razor.css, the exemption stops describing reality:
		// EveryRoutablePageHasAStylesheet never sees the page again to check it, because it is
		// filtered out before the "does a stylesheet exist" check even runs. An entry must be
		// removed the same day the file it names stops being true, not left to rot.
		var stale = PagesWithoutStylesheetByDesign
			.Where(r => File.Exists(Path.Join(ClientSource.RazorRoot, r) + ".css"))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(stale).IsEmpty()
			.Because("a page that now has a stylesheet must be checked by EveryRoutablePageHasAStylesheet, "
				+ "not silently exempted from it");
	}

	// ── Reachability: selectors that compile fine and match nothing ──────────────────────────────
	//
	// Both rules below exist because the same two mistakes shipped repeatedly on this branch and
	// were each caught only by someone opening a browser. Neither produces a warning, a build
	// error, or a visible difference in the file — a dead rule looks exactly like a live one.

	/// <summary>
	/// The innermost rules of a stylesheet: those whose body holds declarations rather than more
	/// rules. Nested at-rule headers (<c>@media</c>, <c>@container</c>, <c>@supports</c>) never
	/// surface as a rule of their own — their body contains braces, so the pattern cannot close on
	/// them and the match restarts inside the block instead. Callers that need the at-rule as a
	/// unit ask for it separately.
	/// </summary>
	private static IEnumerable<(string Prelude, string Body)> Rules(string css) =>
		Regex.Matches(css, @"(?<prelude>[^{}]+)\{(?<body>[^{}]*)\}")
			.Select(m => (Prelude: m.Groups["prelude"].Value.Trim(), Body: m.Groups["body"].Value))
			.Where(rule => rule.Prelude.Length > 0 && rule.Prelude[0] != '@');

	private static IEnumerable<string> Selectors(string css) =>
		Rules(css)
			.SelectMany(rule => rule.Prelude.Split(','))
			.Select(selector => selector.Trim())
			.Where(selector => selector.Length > 0);

	/// <summary>
	/// The classes on a selector's <em>subject</em> — the rightmost compound, the element the rule
	/// actually styles. This is the compound that matters for CSS isolation: Blazor appends the
	/// scope attribute to the last compound (<c>.a .b</c> becomes <c>.a .b[b-xyz]</c>, verified
	/// against the generated <c>.rz.scp.css</c>), so only the subject has to be an element this
	/// component's own markup declares. Ancestor compounds are ordinary matching and need nothing.
	/// </summary>
	private static IEnumerable<string> SubjectClasses(string selector)
	{
		var subject = Regex.Split(selector.Trim(), @"[\s>+~]+").Last();
		// Pseudo-classes and pseudo-elements are dropped whole, arguments included, so a class
		// mentioned inside :not(...) is never mistaken for the subject's own class.
		subject = Regex.Replace(subject, @"::?[A-Za-z-]+(\([^)]*\))?", string.Empty);
		return Regex.Matches(subject, @"\.(?<name>[A-Za-z][\w-]*)").Select(m => m.Groups["name"].Value);
	}

	/// <summary>
	/// Every <c>class="…"</c> / <c>Class="…"</c> value in a .razor, with the tag that carries it and
	/// the value's span in the source. The tag is found by walking forward and skipping each tag's
	/// quoted attribute values wholesale, so a generic type argument (<c>T="List&lt;Thing&gt;"</c>)
	/// is never read as a nested tag — which would attribute the Class beside it to the wrong owner.
	/// </summary>
	private static IEnumerable<(string Tag, int Start, int Length)> ClassAttributeValues(string razor)
	{
		var i = 0;

		while (i < razor.Length)
		{
			if (razor[i] != '<' || i + 1 >= razor.Length || !char.IsLetter(razor[i + 1]))
			{
				i++;
				continue;
			}

			var nameEnd = i + 1;
			while (nameEnd < razor.Length && (char.IsLetterOrDigit(razor[nameEnd]) || razor[nameEnd] is '_' or '.'))
				nameEnd++;

			var tag = razor[(i + 1)..nameEnd];
			var end = nameEnd;
			var quote = '\0';

			while (end < razor.Length)
			{
				var c = razor[end];
				if (quote != '\0')
				{
					if (c == quote) quote = '\0';
				}
				else if (c is '"' or '\'')
				{
					quote = c;
				}
				else if (c == '>')
				{
					break;
				}

				end++;
			}

			var attributes = razor[nameEnd..Math.Min(end, razor.Length)];
			foreach (Match m in Regex.Matches(attributes, @"(?<![\w-])class\s*=\s*""(?<value>[^""]*)""", RegexOptions.IgnoreCase))
				yield return (tag, nameEnd + m.Groups["value"].Index, m.Groups["value"].Length);

			i = end + 1;
		}
	}

	/// <summary>
	/// The literal class names in a <c>Class="…"</c> value, with every Razor expression removed.
	/// A class chosen by C# — <c>@(_on ? "a" : "b")</c>, <c>@_cls</c>, an interpolated name — is
	/// not something this check can read, and inventing names from an expression is how a lint
	/// starts crying wolf and gets switched off.
	/// </summary>
	private static IEnumerable<string> LiteralClassTokens(string value)
	{
		var literal = new StringBuilder();
		var i = 0;

		while (i < value.Length)
		{
			if (value[i] != '@')
			{
				literal.Append(value[i]);
				i++;
				continue;
			}

			i++;
			if (i < value.Length && value[i] == '(')
			{
				var depth = 0;
				while (i < value.Length)
				{
					if (value[i] == '(')
					{
						depth++;
					}
					else if (value[i] == ')' && --depth == 0)
					{
						i++;
						break;
					}

					i++;
				}

				continue;
			}

			while (i < value.Length && (char.IsLetterOrDigit(value[i]) || value[i] is '_' or '.'))
				i++;
		}

		return literal.ToString()
			.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
			.Where(token => Regex.IsMatch(token, @"^[A-Za-z][\w-]*$"));
	}

	/// <summary>
	/// Class names this .razor hands to a component and mentions nowhere else. Blazor stamps the
	/// scope attribute only on elements the component's own markup declares; a <c>Class</c>
	/// parameter is passed to the component, which puts it on markup carrying a different scope.
	///
	/// "Mentions nowhere else" is deliberately blunt: the name is cleared only if every occurrence
	/// in the file sits inside a component <c>Class</c> literal. A class that also appears on a
	/// plain element, or in a C# helper that builds it, or anywhere else at all, is left alone —
	/// this check would rather miss a dead rule than report a live one.
	/// </summary>
	private static IReadOnlySet<string> ClassesOnlyComponentsReceive(string razor)
	{
		var candidates = new HashSet<string>(StringComparer.Ordinal);
		var masked = new StringBuilder(razor);

		foreach (var (tag, start, length) in ClassAttributeValues(razor))
		{
			if (!char.IsUpper(tag[0]))
				continue;

			foreach (var token in LiteralClassTokens(razor.Substring(start, length)))
				candidates.Add(token);

			for (var i = start; i < start + length; i++)
				masked[i] = ' ';
		}

		var elsewhere = masked.ToString();
		candidates.RemoveWhere(name => Regex.IsMatch(elsewhere, $@"(?<![\w-]){Regex.Escape(name)}(?![\w-])"));
		return candidates;
	}

	/// <summary>
	/// Scoped selectors that can never match anything, one message per selector.
	/// </summary>
	private static IEnumerable<string> UnreachableScopedSelectors(string css, string razor)
	{
		var componentOnly = ClassesOnlyComponentsReceive(razor);
		if (componentOnly.Count == 0)
			yield break;

		foreach (var selector in Selectors(css))
		{
			// ::deep anywhere moves the scope attribute off the subject and onto the compound
			// before it, so the subject is unscoped and free to match a component's own markup —
			// which is exactly the fix this rule asks for.
			if (selector.Contains("::deep", StringComparison.Ordinal))
				continue;

			var dead = SubjectClasses(selector)
				.Where(componentOnly.Contains)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
				.ToList();

			if (dead.Count > 0)
				yield return $"`{selector}` — {string.Join(", ", dead.Select(name => "." + name))} "
					+ "only ever lands on a component's own markup, which this scope attribute never reaches; "
					+ "lead with `::deep` anchored on an element this file declares";
		}
	}

	[Test]
	public async Task ScopedSelectorsDoNotTargetClassesOnlyAComponentEverReceives()
	{
		var offenders = ScopedPairs()
			.SelectMany(pair => UnreachableScopedSelectors(StripComments(File.ReadAllText(pair.Css)), File.ReadAllText(pair.Razor))
				.Select(message => $"{Rel(pair.Css)}: {message}"))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("this exact mistake shipped four separate times on this branch — an empty-state icon, a "
				+ "WelcomeTextWidget image, the AdminAccounts MudTable, and .charcreate-card — and every one of "
				+ "them compiled clean, matched nothing, and was found only by looking in a browser");
	}

	[Test]
	public async Task AClassOnlyAComponentReceivesIsRecognisedAsUnreachable()
	{
		// The .charcreate-card shape, reduced: MudPaper takes the class, so the scoped selector
		// resolves against an element that never carries the scope attribute.
		const string razor = """
			<div class="cc-page">
			    <MudPaper Class="charcreate-card">
			        <span class="charcreate-title">x</span>
			    </MudPaper>
			</div>
			""";
		const string css = """
			.cc-page { padding: 1rem; }
			.charcreate-card { border-radius: 8px; }
			.charcreate-card .charcreate-title { font-weight: 600; }
			.cc-page ::deep .charcreate-card { box-shadow: none; }
			""";

		var offenders = UnreachableScopedSelectors(css, razor).ToList();

		await Assert.That(offenders.Count).IsEqualTo(1)
			.Because("only the bare `.charcreate-card` rule is dead: `.charcreate-card .charcreate-title` "
				+ "styles a plain <span> and merely *reads* the component class as an ancestor, and the "
				+ "::deep rule is the sanctioned fix");
		await Assert.That(offenders[0]).Contains("`.charcreate-card`");
		await Assert.That(offenders[0]).Contains("::deep");
	}

	[Test]
	public async Task ClassOriginsRefuseToGuessWhenTheClassCouldBeReachable()
	{
		// The four ways this check could cry wolf, all in one fixture. A rule that reports any of
		// these gets switched off, which leaves the branch exactly as unprotected as no rule at all.
		const string razor = """
			<div class="dual">a plain element and a component both carry this one</div>
			<MudPaper Class="dual" />
			<MudChip Class="@(_on ? "computed-on" : "computed-off")" />
			<MudCard Class="@($"tile tile--{_size}")" />
			<MudChip Class="from-helper" />
			<MudTable T="List<Thing>" Class="admin-accounts-table" />
			@code {
			    private void Apply(ElementReference e) => Js.InvokeVoidAsync("addClass", e, "from-helper");
			}
			""";

		var componentOnly = ClassesOnlyComponentsReceive(razor);

		await Assert.That(componentOnly).DoesNotContain("dual")
			.Because("a class on both a component and a plain element still matches on the plain element");
		await Assert.That(componentOnly).DoesNotContain("computed-on")
			.Because("a class chosen by a C# expression is not a literal this check may read");
		await Assert.That(componentOnly).DoesNotContain("tile")
			.Because("an interpolated Class value names no class the checker can be sure of");
		await Assert.That(componentOnly).DoesNotContain("from-helper")
			.Because("a name that also appears in C# may be applied from there to a plain element");
		await Assert.That(componentOnly).Contains("admin-accounts-table")
			.Because("a literal Class on a component tag, mentioned nowhere else, cannot be reached — "
				+ "and the generic type argument beside it must not confuse the owning tag");
	}

	/// <summary>
	/// Classes whose own rule establishes a query container. Only the subject compound counts:
	/// <c>container-type</c> applies to the element the rule styles.
	/// </summary>
	private static IReadOnlySet<string> ContainerDeclaringClasses(string css) =>
		Rules(css)
			.Where(rule => Regex.IsMatch(rule.Body, @"(^|[;\s])container(-type)?\s*:"))
			.SelectMany(rule => rule.Prelude.Split(','))
			.SelectMany(SubjectClasses)
			.ToHashSet(StringComparer.Ordinal);

	/// <summary>
	/// Selectors inside <em>unnamed</em> <c>@container</c> blocks — a component querying the
	/// container it declared itself. A named <c>@container page (…)</c> query is a different
	/// relationship entirely (the shell's container, always an ancestor) and never appears here.
	/// </summary>
	private static IEnumerable<string> UnnamedContainerSelectors(string css)
	{
		foreach (Match m in Regex.Matches(css, @"@container\s*\("))
		{
			var open = css.IndexOf('{', m.Index);
			if (open < 0)
				continue;

			var depth = 0;
			var close = open;
			for (; close < css.Length; close++)
			{
				if (css[close] == '{')
					depth++;
				else if (css[close] == '}' && --depth == 0)
					break;
			}

			foreach (var selector in Selectors(css[(open + 1)..Math.Min(close, css.Length)]))
				yield return selector;
		}
	}

	/// <summary>
	/// Rules inside a component's own <c>@container</c> block whose subject is the very element
	/// that declares the container.
	/// </summary>
	private static IEnumerable<string> SelfContainerQueries(string css)
	{
		var containers = ContainerDeclaringClasses(css);
		if (containers.Count == 0)
			yield break;

		foreach (var selector in UnnamedContainerSelectors(css))
		{
			// A combinator means the subject is some other element and the container class, if
			// present, is only an ancestor qualifier — `.widget ::deep .mud-grid-item` matches
			// perfectly well, because matching an ancestor has nothing to do with querying it.
			if (Regex.IsMatch(selector, @"[\s>+~]"))
				continue;

			var self = SubjectClasses(selector)
				.Where(containers.Contains)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
				.ToList();

			if (self.Count > 0)
				yield return $"`{selector}` — {string.Join(", ", self.Select(name => "." + name))} declares "
					+ "container-type on this same element, and an element is never its own query container; "
					+ "move containment to a wrapper so the root becomes a descendant of it";
		}
	}

	[Test]
	public async Task NoComponentQueriesAContainerItDeclaresOnItself()
	{
		var offenders = ScopedStylesheets()
			.SelectMany(file => SelfContainerQueries(StripComments(File.ReadAllText(file)))
				.Select(message => $"{Rel(file)}: {message}"))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("such a rule resolves against the nearest *ancestor* container instead — .phosphor-page "
				+ "when the component sits on a page, and nothing at all in a footer or a widget aside — so "
				+ "every descendant rule in the block fires while the root's own rule silently does not");
	}

	[Test]
	public async Task AnElementQueryingItsOwnContainerIsRecognised()
	{
		// The WikiIndexWidget shape, reduced — including the `.widget.widget` doubling that was
		// written to buy specificity and only made the dead rule harder to spot.
		const string css = """
			.widget { container-type: inline-size; padding: 2rem; }
			.widget-body { padding: 1rem; }
			@container (max-width: 30rem) {
				.widget.widget { padding: 1rem; }
				.widget .widget-body { padding: 0; }
				.widget ::deep .mud-grid-item { flex: 1 1 100%; }
			}
			@container page (max-width: 48rem) {
				.widget { padding: 0; }
			}
			""";

		var offenders = SelfContainerQueries(css).ToList();

		await Assert.That(offenders.Count).IsEqualTo(1)
			.Because("the descendant rules query the container correctly, and a named `@container page` "
				+ "query asks the shell's container — an ancestor — which is always legitimate");
		await Assert.That(offenders[0]).Contains("`.widget.widget`");
		await Assert.That(offenders[0]).Contains("wrapper");
	}
}
