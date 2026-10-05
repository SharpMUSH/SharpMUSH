using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

public class ContentContainerTests
{
	private static string Shell() =>
		File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));

	private static string MainLayout() =>
		File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Layout", "MainLayout.razor"));

	[Test]
	public async Task TheBodyIsWrappedInTheNamedQueryContainer()
	{
		await Assert.That(MainLayout()).Contains("phosphor-page")
			.Because("pages can only use @container if something declares the container around @Body");

		var rule = Regex.Match(Shell(), @"\.phosphor-page\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
		await Assert.That(rule.Success).IsTrue();
		await Assert.That(rule.Groups["body"].Value).Contains("container: page / inline-size")
			.Because("the container must be named 'page' and query the inline axis only");
	}

	[Test]
	public async Task FullHeightPagesKeepADefiniteHeightThroughTheWrapper()
	{
		// A descendant, not a child: a section's page sits inside SectionShell's .kit-section-body.
		var height = Regex.Match(Shell(), @"\.phosphor-page:has\(\.full-height\)\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
		await Assert.That(height.Success).IsTrue().Because("full-height pages need a selector that passes a definite height through the wrapper");
		await Assert.That(height.Groups["body"].Value).Contains("height: 100%");
	}

	[Test]
	public async Task ThePageFillsTheContentColumnAtEveryWidth()
	{
		var shell = Shell();
		await Assert.That(Regex.IsMatch(shell, @"\.phosphor-page(?![-\w])[^{]*\{[^}]*max-width", RegexOptions.Singleline)).IsFalse()
			.Because("a capped page floats away from the sidebar on a wide screen; a page that reads better "
				+ "narrower caps its own block instead");
		await Assert.That(Regex.IsMatch(shell, @"\.phosphor-page(?![-\w])[^{]*\{[^}]*margin-inline:\s*auto", RegexOptions.Singleline)).IsFalse()
			.Because("the page starts against the sidebar, not centred in the column");
	}

	[Test]
	public async Task TheSectionBody_PassesTheHeightOnToAFullHeightPage()
	{
		// /softcode sits in Build & manage: page → .kit-section-body → .sc-shell (height: 100%). Without
		// a definite height on the section body the editor's panes and Monaco collapse.
		var css = File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Components", "Kit", "SectionShell.razor.css"));
		var rule = Regex.Match(css, @"\.kit-section-body:has\(> \.full-height\)\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
		await Assert.That(rule.Success).IsTrue();
		await Assert.That(rule.Groups["body"].Value).Contains("height: 100%");
	}
}
