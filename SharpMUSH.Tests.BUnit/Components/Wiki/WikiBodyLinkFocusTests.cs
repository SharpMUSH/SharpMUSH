using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Components.Wiki;

public class WikiBodyLinkFocusTests
{
	[Test]
	public async Task EveryWikiBodyLinkShowsTheAccentFocusRing()
	{
		var css = File.ReadAllText(Path.Join(ClientSource.RazorRoot, "Components", "WikiDisplay.razor.css"));
		var rule = Regex.Match(css, @"\.WikiContent\s+::deep\s+a:focus-visible\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
		await Assert.That(rule.Success).IsTrue()
			.Because("a keyboard reader must see which body link has focus, not only the next-step link");
		await Assert.That(rule.Groups["body"].Value).Contains("outline: 2px solid var(--accent)");
	}
}
