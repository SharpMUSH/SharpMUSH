using System.Text.RegularExpressions;
using MarkupString.Ansi;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The HTML emitters write bare <c>ms-*</c> classes for every attribute with a fixed rendering, and
/// <see cref="AnsiCss.Fixed"/> is the package's own stylesheet for them. The portal does not include
/// that string — it carries its own copy in shell.css — so a class added to the package, or a value
/// changed in one, would silently diverge from what the terminal renders. This pins the two
/// together: every rule the package defines must appear in shell.css with the same declarations.
/// The portal may add rules of its own (the combined underline+strike, the link classes); it may
/// not restate one of these with a different value.
/// </summary>
public class MarkupCssCoverageTests
{
	private static readonly string ShellCss = File.ReadAllText(Path.Join(ClientSource.CssRoot, "shell.css"));

	/// <summary>
	/// Every (selector, declaration) pair in <see cref="AnsiCss.Fixed"/>. A nested block — the
	/// <c>@keyframes</c> body — survives as a single declaration, because the split only cuts on
	/// semicolons outside braces.
	/// </summary>
	public static IEnumerable<Func<(string Selector, string Declaration)>> FixedDeclarations() =>
		CssRules(AnsiCss.Fixed)
			.SelectMany(rule => Declarations(rule.Body).Select(d => (rule.Selector, Declaration: d)))
			.Select(Func<(string, string)> (pair) => () => pair);

	[Test]
	[MethodDataSource(nameof(FixedDeclarations))]
	public async Task EveryFixedDeclaration_IsInShellCss((string Selector, string Declaration) rule)
	{
		var shellDeclarations = CssRules(ShellCss)
			.Where(r => string.Equals(r.Selector, rule.Selector, StringComparison.Ordinal))
			.SelectMany(r => Declarations(r.Body))
			.ToArray();

		await Assert.That(shellDeclarations).Contains(rule.Declaration)
			.Because($"AnsiCss.Fixed declares `{rule.Selector} {{ {rule.Declaration}; }}` and shell.css must match it");
	}

	/// <summary>
	/// A guard on the guard: if the rule scrape ever comes back empty or loses the nested keyframes,
	/// every case above passes vacuously and the pin is gone.
	/// </summary>
	[Test]
	public async Task TheRuleScrape_FindsTheKnownRules()
	{
		var pairs = FixedDeclarations().Select(f => f()).ToArray();
		var selectors = pairs.Select(p => p.Selector).Distinct(StringComparer.Ordinal).ToArray();

		await Assert.That(selectors).Contains(".ms-bold");
		await Assert.That(selectors).Contains(".ms-invert");
		await Assert.That(selectors).Contains("@keyframes ms-blink");
		await Assert.That(selectors.Length).IsGreaterThanOrEqualTo(8);
		await Assert.That(pairs.Select(p => p.Declaration)).Contains("font-weight: bold");
	}

	/// <summary>
	/// Splits a stylesheet into its top-level rules, dropping comments and collapsing runs of
	/// whitespace, so a rule written over several lines compares equal to the same rule on one.
	/// Braces are matched, so a rule with a nested block keeps that block in its body.
	/// </summary>
	private static IEnumerable<(string Selector, string Body)> CssRules(string css)
	{
		var text = Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);
		var start = 0;

		for (var i = 0; i < text.Length; i++)
		{
			if (text[i] != '{')
			{
				continue;
			}

			var selector = Normalise(text[start..i]);
			var bodyStart = i + 1;
			var depth = 1;

			while (++i < text.Length && depth > 0)
			{
				if (text[i] == '{')
				{
					depth++;
				}
				else if (text[i] == '}')
				{
					depth--;
				}
			}

			// An unbalanced trailing block is not a rule. Stopping here keeps the scrape honest
			// rather than inventing a body that ran off the end of the sheet.
			if (depth > 0)
			{
				yield break;
			}

			yield return (selector, Normalise(text[bodyStart..(i - 1)]));
			start = i;
		}
	}

	/// <summary>The body's declarations, split on the semicolons outside any nested block.</summary>
	private static IEnumerable<string> Declarations(string body)
	{
		var depth = 0;
		var start = 0;

		for (var i = 0; i < body.Length; i++)
		{
			switch (body[i])
			{
				case '{':
					depth++;
					break;
				case '}':
					depth--;
					break;
				case ';' when depth == 0:
					if (Normalise(body[start..i]) is { Length: > 0 } declaration)
					{
						yield return declaration;
					}

					start = i + 1;
					break;
			}
		}

		if (Normalise(body[start..]) is { Length: > 0 } last)
		{
			yield return last;
		}
	}

	private static string Normalise(string fragment) => Regex.Replace(fragment, @"\s+", " ").Trim();
}
