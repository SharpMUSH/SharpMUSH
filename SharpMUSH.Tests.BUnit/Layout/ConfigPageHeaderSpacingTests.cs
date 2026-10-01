using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// The hand-built config pages (Banned Names, Import, Restrictions, Sitelock) put a
/// <c>PlainPageHeader</c> straight into a plain <c>.config-section-page</c> block. The kit header
/// carries no bottom margin, so the space under it has to come from the config layout, once, for
/// every such page — otherwise the header sits flush against the first card or tab strip.
/// </summary>
public class ConfigPageHeaderSpacingTests
{
	private static readonly Regex HeaderSpacingRule = new(
		@"\.config-section-page\s*>\s*\.kit-page-head\s*\{[^}]*margin-bottom\s*:",
		RegexOptions.Compiled);

	private static IEnumerable<string> SectionPagesWithPlainHeader() =>
		Directory.EnumerateFiles(Path.Join(ClientSource.RazorRoot, "Pages"), "*.razor", SearchOption.AllDirectories)
			.Where(f =>
			{
				var text = File.ReadAllText(f);
				return text.Contains("class=\"config-section-page\"") && text.Contains("<PlainPageHeader");
			});

	[Test]
	public async Task ConfigLayoutSpacesThePlainHeaderOfASectionPage()
	{
		// ConfigLayout renders through SectionShell, whose body the rule is anchored on.
		var layoutCss = File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Components", "Kit", "SectionShell.razor.css"));

		await Assert.That(HeaderSpacingRule.IsMatch(layoutCss)).IsTrue();
	}

	[Test]
	public async Task EverySectionPageWithAPlainHeaderUsesTheConfigLayout()
	{
		var pages = SectionPagesWithPlainHeader().ToList();
		var outsideLayout = pages
			.Where(f => !Regex.IsMatch(File.ReadAllText(f), @"^[ \t]*@layout\s+ConfigLayout\b", RegexOptions.Multiline))
			.Select(f => Path.GetRelativePath(ClientSource.RazorRoot, f).Replace('\\', '/'))
			.ToList();

		await Assert.That(pages.Count).IsGreaterThanOrEqualTo(4);
		await Assert.That(outsideLayout).IsEmpty();
	}
}
