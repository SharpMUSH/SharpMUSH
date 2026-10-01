using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// README §3: with the rail and the page sidebar open, a 1280px window leaves the page 976px — the
/// medium tier. The boards keep main and aside side by side there; only the narrow tier (below 48rem)
/// stacks the aside under main. A page that stacked at 64rem would sit stacked at the design size.
/// </summary>
public class FrameTierTests
{
	public static IEnumerable<(string File, string Selector)> MainAndAsidePages() =>
	[
		("Components/WikiDisplay.razor.css", ".wiki-page"),
		("Pages/CharacterProfile.razor.css", ".char-profile-body"),
		("Pages/WikiIndex.razor.css", ".wiki-index-page"),
		("Pages/Admin/Config/ConfigIndex.razor.css", ".config-home"),
		("Pages/Admin/Config/DynamicConfig.razor.css", ".config-section"),
	];

	[Test]
	[MethodDataSource(nameof(MainAndAsidePages))]
	public async Task MainAndAside_StackOnlyAtTheNarrowTier((string File, string Selector) page)
	{
		var css = File.ReadAllText(Path.Join(ClientSource.RazorRoot, page.File));
		var stacking = new Regex(Regex.Escape(page.Selector) + @"\s*\{\s*grid-template-columns:\s*minmax\(0,\s*1fr\);");

		foreach (Match block in Regex.Matches(css, @"@container page \(max-width: (?<tier>\d+)rem\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}"))
		{
			if (stacking.IsMatch(block.Groups["body"].Value))
			{
				await Assert.That(block.Groups["tier"].Value).IsEqualTo("48")
					.Because($"{page.File}: {page.Selector} stacks main and aside, which README §3 keeps side by side at the medium tier");
				return;
			}
		}

		throw new InvalidOperationException($"{page.File} never stacks {page.Selector}; the narrow tier must.");
	}

	[Test]
	public async Task TheWikiHome_KeepsItsHeroSearchAndThreeColumns_InTheMediumTiersMainColumn()
	{
		// Board 21 at 1280×800 with the sidebar open: main is about 740px beside the aside, and the hero
		// search sits beside the blurb over three category columns. The widget queries its own width, so
		// its first step down must be below 740px (46.25rem).
		var css = File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Components/Widgets/WikiIndexWidget.razor.css"));
		foreach (Match block in Regex.Matches(css, @"@container \(max-width: (?<rem>[\d.]+)rem\)\s*\{(?<body>(?:[^{}]|\{[^{}]*\})*)\}"))
		{
			if (block.Groups["body"].Value.Contains(".wiki-hero-row") || block.Groups["body"].Value.Contains("repeat(2, minmax(0, 1fr))"))
			{
				await Assert.That(double.Parse(block.Groups["rem"].Value, System.Globalization.CultureInfo.InvariantCulture)).IsLessThan(46.25);
			}
		}
	}
}
