using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The content column's width is not a function of the viewport: the sidebar collapses and both
/// widget asides are sized from runtime admin settings (MainLayout.razor). Pages therefore size
/// against a query container rather than the screen, and this fixes the one place that container
/// is declared.
/// </summary>
public class ContentContainerTests
{
	private static string Shell() =>
		File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "css", "shell.css"));

	private static string MainLayout() =>
		File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "razor", "Layout", "MainLayout.razor"));

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
		// Inserting a wrapper between .phosphor-main and the page would otherwise break the pages
		// that set height:100% on their own root (/play, the wiki editor).
		await Assert.That(Shell()).Contains(":has(> .full-bleed)")
			.Because("full-bleed pages opt out of the cap and need the wrapper to pass height through");
	}

	[Test]
	public async Task WideViewportsCapTheReadingWidth()
	{
		var wide = Regex.Match(Shell(), @"@media\s*\(min-width:\s*1601px\)\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline);
		await Assert.That(wide.Success).IsTrue().Because("the ultrawide tier must exist");
		await Assert.That(wide.Groups["body"].Value).Contains("--content-max")
			.Because("the cap reads a token rather than hardcoding a width");
		await Assert.That(wide.Groups["body"].Value).Contains("margin-inline: auto")
			.Because("capped content is centred, not left-aligned against the sidebar");
	}
}
