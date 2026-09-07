using System.Text.RegularExpressions;
using MarkupString.Html;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The HTML emitters write bare <c>ms-*</c> classes for every attribute with a fixed rendering, and
/// <see cref="HtmlCss.Fixed"/> is the package's own stylesheet for them. The portal does not include
/// that string — it carries its own themed copies in shell.css — so a class added to the package
/// would render as unstyled text in the terminal until someone noticed. This pins the two together:
/// every selector the package defines must have a rule in shell.css.
/// </summary>
public class MarkupCssCoverageTests
{
	private static readonly string ShellCss = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));

	/// <summary>Every <c>.ms-*</c> selector at the start of a rule in <see cref="HtmlCss.Fixed"/>.</summary>
	public static IEnumerable<Func<string>> FixedSelectors() =>
		Regex.Matches(HtmlCss.Fixed, @"^\s*(\.ms-[a-z0-9-]+)", RegexOptions.Multiline)
			.Select(m => m.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.Select(Func<string> (s) => () => s);

	[Test]
	[MethodDataSource(nameof(FixedSelectors))]
	public async Task EveryFixedSelector_HasARuleInShellCss(string selector)
		=> await Assert.That(Regex.IsMatch(ShellCss, $@"(?m)^\s*{Regex.Escape(selector)}[\s,{{.:]"))
			.IsTrue()
			.Because($"{selector} is written by the HTML emitters but shell.css has no rule for it");

	/// <summary>
	/// A guard on the guard: if the selector scrape ever comes back empty, every case above passes
	/// vacuously and the pin is gone.
	/// </summary>
	[Test]
	public async Task TheSelectorScrape_FindsTheKnownClasses()
	{
		var selectors = FixedSelectors().Select(f => f()).ToArray();

		await Assert.That(selectors).Contains(".ms-bold");
		await Assert.That(selectors).Contains(".ms-invert");
		await Assert.That(selectors.Length).IsGreaterThanOrEqualTo(8);
	}
}
