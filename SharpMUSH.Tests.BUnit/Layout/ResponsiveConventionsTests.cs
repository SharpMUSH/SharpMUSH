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

	private static string Rel(string path) =>
		Path.GetRelativePath(ClientSource.RazorRoot, path).Replace('\\', '/');

	/// <summary>
	/// Every rule below matches on CSS syntax, and this codebase documents its stylesheets heavily —
	/// including prose that names the very at-rules being banned. Scanning raw text would fail a file
	/// for explaining the convention it follows.
	/// </summary>
	private static string StripComments(string css) =>
		Regex.Replace(css, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);

	/// <summary>
	/// Stylesheets still on the old viewport-query model. Each sweep batch deletes its own entries;
	/// the list must reach empty, which <see cref="TheMigrationIsFinished"/> asserts.
	/// </summary>
	private static readonly HashSet<string> NotYetMigrated = new(StringComparer.Ordinal)
	{
		"Components/Layout/ZoneRenderer.razor.css",
		"Components/ScenePoseLine.razor.css",
		"Components/Widgets/RecentWikiActivityWidget.razor.css",
		"Components/Widgets/WikiBodyWidget.razor.css",
		"Components/Widgets/WikiIndexWidget.razor.css",
		"Components/WikiDisplay.razor.css",
		"Components/WikiEdit.razor.css",
		"Layout/AccountPanel.razor.css",
		"Layout/ConfigLayout.razor.css",
		"Pages/Account.razor.css",
		"Pages/Admin/AdminConfig.razor.css",
		"Pages/Admin/Applications/AdminApplications.razor.css",
		"Pages/Admin/Config/DynamicConfig.razor.css",
		"Pages/Admin/Layout/AdminLayouts.razor.css",
		"Pages/Admin/Layout/LayoutEditor.razor.css",
		"Pages/Admin/Packages/AdminPackageAuthor.razor.css",
		"Pages/Admin/Packages/AdminPackageBrowse.razor.css",
		"Pages/Admin/Packages/AdminPackageRemotes.razor.css",
		"Pages/Admin/Packages/AdminPackageReview.razor.css",
		"Pages/Admin/Packages/AdminPackages.razor.css",
		"Pages/Admin/Restrictions.razor.css",
		"Pages/Admin/Roles/AdminRoles.razor.css",
		"Pages/Admin/Sitelock.razor.css",
		"Pages/CharacterProfile.razor.css",
		"Pages/Characters.razor.css",
		"Pages/DynamicApplication.razor.css",
		"Pages/Help.razor.css",
		"Pages/HelpAdminTopic.razor.css",
		"Pages/HelpTopic.razor.css",
		"Pages/Home.razor.css",
		"Pages/Login.razor.css",
		"Pages/Mail.razor.css",
		"Pages/MailCompose.razor.css",
		"Pages/MailDetail.razor.css",
		"Pages/Play.razor.css",
		"Pages/SceneDetail.razor.css",
		"Pages/SceneLive.razor.css",
		"Pages/Scenes.razor.css",
		"Pages/ScenesActive.razor.css",
		"Pages/Settings.razor.css",
		"Pages/SettingsTheme.razor.css",
		"Pages/Setup.razor.css",
		"Pages/SoftcodeEditor.razor.css",
		"Pages/WikiPageDiff.razor.css",
		"Pages/WikiPageHistory.razor.css",

		// No @media to convert, but !important to remove, so they are exempt until their batch
		// reaches them — MigratedStylesheetsCarryNoImportantDeclarations reads this same list.
		"Components/Widgets/CharacterDirectoryWidget.razor.css",
		"Pages/Admin/AdminMedia.razor.css",
		"Pages/Admin/AdminServer.razor.css",
		"Pages/Admin/ImportDatabase.razor.css",
	};

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

		// Not by design — these gain stylesheets during the sweep and their entries are
		// deleted with the task that writes them.
		"Pages/Admin/AdminAccounts.razor",   // Task 7
		"Pages/WikiPage.razor",              // Task 12
		"Pages/WikiPageEdit.razor",          // Task 12
		"Pages/CharacterCreate.razor",       // Task 13
		"Pages/Register.razor",              // Task 13
	};

	private static readonly string[] SanctionedTiers = ["48rem", "64rem", "90rem"];

	[Test]
	public async Task PagesQueryTheirContainerRatherThanTheViewport()
	{
		var offenders = ScopedStylesheets()
			.Where(f => !NotYetMigrated.Contains(Rel(f)))
			.Where(f => Regex.IsMatch(StripComments(File.ReadAllText(f)), @"@media\b"))
			.Select(Rel)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("a page cannot see the sidebar or the admin-configured widget asides, so a "
				+ "viewport query inside one is wrong whenever either is present; use @container page");
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

	[Test]
	public async Task ContainerTiersUseOnlyTheSanctionedLiterals()
	{
		var offenders = new List<string>();

		foreach (var file in ScopedStylesheets())
		{
			foreach (Match m in Regex.Matches(StripComments(File.ReadAllText(file)), @"@container[^{]*?\(\s*(?:min|max)-width:\s*(?<value>[^)]+)\)"))
			{
				var value = m.Groups["value"].Value.Trim();
				if (!SanctionedTiers.Contains(value))
					offenders.Add($"{Rel(file)}: {value}");
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because($"tiers drift into a private set of breakpoints unless they are fixed at {string.Join(" / ", SanctionedTiers)}");
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
			.Where(f => !NotYetMigrated.Contains(Rel(f)))
			.Where(f => StripComments(File.ReadAllText(f)).Contains("!important", StringComparison.Ordinal))
			.Select(Rel)
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(offenders).IsEmpty()
			.Because("unlayered scoped CSS already beats the vendor layer; !important hides real conflicts");
	}

	[Test]
	public async Task EveryRoutablePageHasAStylesheet()
	{
		var offenders = Directory.EnumerateFiles(Path.Join(ClientSource.RazorRoot, "Pages"), "*.razor", SearchOption.AllDirectories)
			.Where(f => Regex.IsMatch(File.ReadAllText(f), @"^@page\b", RegexOptions.Multiline))
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
	public async Task TheExemptionListHasNoStaleEntries()
	{
		var missing = NotYetMigrated
			.Where(r => !File.Exists(Path.Join(ClientSource.RazorRoot, r)))
			.Order(StringComparer.Ordinal)
			.ToList();

		await Assert.That(missing).IsEmpty()
			.Because("an entry naming a file that no longer exists silently exempts nothing and hides progress");
	}

	[Test, Skip("Enabled by the final sweep task, once NotYetMigrated is empty.")]
	public async Task TheMigrationIsFinished()
	{
		await Assert.That(NotYetMigrated).IsEmpty()
			.Because("the exemption list is a burn-down, not a permanent allowance");
	}
}
