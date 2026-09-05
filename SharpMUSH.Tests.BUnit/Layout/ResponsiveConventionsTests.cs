using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

public class ResponsiveConventionsTests
{
	private static IEnumerable<string> ScopedStylesheets() =>
		Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor.css", SearchOption.AllDirectories);

	private static IEnumerable<string> PageStylesheets() =>
		Directory.EnumerateFiles(Path.Join(ClientSource.RazorRoot, "Pages"), "*.razor.css", SearchOption.AllDirectories)
			.Where(f =>
			{
				var razor = f[..^".css".Length];
				return File.Exists(razor) && Regex.IsMatch(File.ReadAllText(razor), @"^[ \t]*@page\b", RegexOptions.Multiline);
			});

	private static IEnumerable<(string Css, string Razor)> ScopedPairs() =>
		ScopedStylesheets()
			.Select(css => (Css: css, Razor: css[..^".css".Length]))
			.Where(pair => File.Exists(pair.Razor));

	private static string Rel(string path) =>
		Path.GetRelativePath(ClientSource.RazorRoot, path).Replace('\\', '/');

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

	private static IEnumerable<Match> NamedPageContainerConditions(string css) =>
		NamedPageContainerBlocks(css)
			.SelectMany(header => Regex.Matches(header, @"(?<dir>min|max)-width:\s*(?<value>[^)]+)\)"));

	private static IEnumerable<string> NamedPageContainerBlocks(string css) =>
		Regex.Matches(css, @"@container\s+page\s*(?<header>\([^{]*)\{")
			.Select(block => block.Groups["header"].Value);

	[Test]
	public async Task ContainerTiersUseOnlyTheSanctionedLiterals()
	{
		var offenders = new List<string>();

		foreach (var file in ScopedStylesheets())
		{
			var values = NamedPageContainerConditions(StripComments(File.ReadAllText(file)))
				.Select(m => m.Groups["value"].Value.Trim());
			foreach (var value in values)
			{
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
			var conditions = NamedPageContainerConditions(StripComments(File.ReadAllText(file)))
				.Select(m => (value: m.Groups["value"].Value.Trim(), dir: m.Groups["dir"].Value));
			foreach (var (value, dir) in conditions)
			{
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
		var stale = PagesWithoutStylesheetByDesign
			.Where(r => File.Exists(Path.Join(ClientSource.RazorRoot, r) + ".css"))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(stale).IsEmpty()
			.Because("a page that now has a stylesheet must be checked by EveryRoutablePageHasAStylesheet, "
				+ "not silently exempted from it");
	}

	/// <summary>
	/// Innermost rules only: an at-rule's body contains braces, so the pattern restarts inside the
	/// block rather than yielding the <c>@media</c>/<c>@container</c> header as a rule of its own.
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

	private static IEnumerable<string> SubjectClasses(string selector)
	{
		var subject = Regex.Split(selector.Trim(), @"[\s>+~]+").Last();
		subject = Regex.Replace(subject, @"::?[A-Za-z-]+(\([^)]*\))?", string.Empty);
		return Regex.Matches(subject, @"\.(?<name>[A-Za-z][\w-]*)").Select(m => m.Groups["name"].Value);
	}

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

	private static IEnumerable<string> UnreachableScopedSelectors(string css, string razor)
	{
		var componentOnly = ClassesOnlyComponentsReceive(razor);
		if (componentOnly.Count == 0)
			yield break;

		foreach (var selector in Selectors(css))
		{
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

	private static IReadOnlySet<string> ContainerDeclaringClasses(string css) =>
		Rules(css)
			.Where(rule => Regex.IsMatch(rule.Body, @"(^|[;\s])container(-type)?\s*:"))
			.SelectMany(rule => rule.Prelude.Split(','))
			.SelectMany(SubjectClasses)
			.ToHashSet(StringComparer.Ordinal);

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

	private static IEnumerable<string> SelfContainerQueries(string css)
	{
		var containers = ContainerDeclaringClasses(css);
		if (containers.Count == 0)
			yield break;

		foreach (var selector in UnnamedContainerSelectors(css))
		{
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

	/// <summary>
	/// A pose keeps its line breaks on screen. Poses are prose composed in a multi-line box and the
	/// content is stored with real newlines, but HTML collapses whitespace — so without this the
	/// archive silently ran every line of a pose together, and the newline handling that gets it
	/// there intact was invisible to the reader. pre-wrap keeps the breaks and still wraps long lines.
	/// </summary>
	[TUnit.Core.Test]
	public async Task ScenePoseBody_PreservesLineBreaks()
	{
		var shell = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));
		var rule = Regex.Match(shell, @"\.scene-pose-body\s*\{[^}]*\}", RegexOptions.Singleline);

		await Assert.That(rule.Success).IsTrue().Because(".scene-pose-body needs a rule to hold the whitespace mode");
		await Assert.That(rule.Value).Contains("pre-wrap");
	}
}
