using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using SharpMUSH.Documentation.MarkdownToAsciiRenderer;

namespace SharpMUSH.Tests.Documentation;

/// <summary>
/// A code block shows what a player types and what the game prints, character for character, so nothing in
/// it is Markdown. The conversion from PennMUSH's help wrapped angle brackets in backticks or escaped them
/// with a backslash, which broke examples such as <c>%q&lt;one&gt;</c>: typed as shown, they no longer run.
/// </summary>
public class HelpExampleMarkupTests
{
	[Test]
	public async Task NoCodeBlockEscapesAnAngleBracket()
	{
		var offending = TestPaths.Helpfiles
			.EnumerateFiles("*.md", SearchOption.AllDirectories)
			.SelectMany(file => CodeBlockLines(File.ReadLines(file.FullName))
				.Where(entry => entry.Line.Contains("`<") || entry.Line.Contains(">`") || entry.Line.Contains("\\<"))
				.Select(entry => $"{file.Name}:{entry.Number}: {entry.Line}"))
			.ToList();

		await Assert.That(offending).IsEmpty();
	}

	/// <summary>
	/// An attribute path such as <c>LOG`RADIO</c> holds a backtick, so a single-backtick code span around it
	/// ends at that backtick and the rest of the line shows its markup. A span holding a backtick needs a
	/// longer fence (<c>`` LOG`RADIO ``</c>), and a backtick in prose needs <c>\`</c>.
	/// </summary>
	[Test]
	public async Task NoCodeSpanIsCutShortByABacktick()
	{
		var offending = HelpMarkdownFiles()
			.SelectMany(file => BacktickProblems(File.ReadAllText(file.FullName)).Select(problem => $"{file.Name}: {problem}"))
			.ToList();

		await Assert.That(offending).IsEmpty();
	}

	/// <summary>
	/// A <c>|</c> splits a table cell on GitHub and the Docs site even inside a code span, so a code span in a
	/// table writes it as <c>\|</c>, which the help renderers read as <c>|</c>.
	/// </summary>
	[Test]
	public async Task NoTableCodeSpanHoldsABarePipe()
	{
		var offending = HelpMarkdownFiles()
			.SelectMany(file =>
			{
				var markdown = File.ReadAllText(file.FullName);
				return RecursiveMarkdownHelper.Parse(markdown).Descendants<Table>()
					.SelectMany(table => table.Descendants<CodeInline>())
					.Where(code => HasUnescaped(markdown.Substring(code.Span.Start, code.Span.Length), '|'))
					.Select(code => $"{file.Name}:{code.Line + 1}: `{code.Content}`");
			})
			.ToList();

		await Assert.That(offending).IsEmpty();
	}

	[Test]
	public async Task TableCodeSpanReadsAnEscapedPipeAsAPipe()
	{
		const string markdown = "| Pattern | Meaning |\n|---|---|\n| `a\\|b` | Either. |\n\n`a\\|b` outside.";

		var plain = RecursiveMarkdownHelper.RenderMarkdown(markdown).ToPlainText();
		var html = HelpHtmlRenderer.RenderToHtml(markdown, _ => null);

		await Assert.That(plain).Contains("a|b").And.Contains(@"a\|b outside.");
		await Assert.That(html).Contains("<td><code>a|b</code></td>").And.Contains(@"<code>a\|b</code> outside.");
	}

	/// <summary>The engine's help and the shipped plugins' help, which the same renderers read.</summary>
	private static IEnumerable<FileInfo> HelpMarkdownFiles() =>
		TestPaths.Helpfiles.EnumerateFiles("*.md", SearchOption.AllDirectories)
			.Concat(new DirectoryInfo(TestPaths.RepositoryRoot).EnumerateDirectories("SharpMUSH.Plugins.*")
				.Select(plugin => new DirectoryInfo(Path.Join(plugin.FullName, "help")))
				.Where(help => help.Exists)
				.SelectMany(help => help.EnumerateFiles("*.md", SearchOption.AllDirectories)));

	/// <summary>
	/// A backtick left as text that was not written <c>\`</c>, or a code span with a capital letter or
	/// <c>_</c> right against it: both are what an attribute path's backtick does to a single-backtick span.
	/// </summary>
	private static IEnumerable<string> BacktickProblems(string markdown)
	{
		var document = RecursiveMarkdownHelper.Parse(markdown);
		foreach (var literal in document.Descendants<LiteralInline>()
			.Where(literal => HasUnescaped(markdown.Substring(literal.Span.Start, literal.Span.Length), '`')))
		{
			yield return $"line {literal.Line + 1}: a backtick shows as text";
		}

		foreach (var code in document.Descendants<CodeInline>())
		{
			if (code.PreviousSibling is LiteralInline { Content.Length: > 0 } before && IsNameCharacter(before.Content.Text[before.Content.End]))
			{
				yield return $"line {code.Line + 1}: `{code.Content}` starts inside \"{before.Content}\"";
			}
			else if (code.NextSibling is LiteralInline { Content.Length: > 0 } after && IsNameCharacter(after.Content.Text[after.Content.Start]))
			{
				yield return $"line {code.Line + 1}: `{code.Content}` ends inside \"{after.Content}\"";
			}
		}
	}

	private static bool HasUnescaped(string source, char character) =>
		source.Select((c, i) => c == character && (i == 0 || source[i - 1] != '\\')).Any(found => found);

	private static bool IsNameCharacter(char c) => c is (>= 'A' and <= 'Z') or '_';

	private static IEnumerable<(string Line, int Number)> CodeBlockLines(IEnumerable<string> lines)
	{
		var inBlock = false;
		var number = 0;
		foreach (var line in lines)
		{
			number++;
			if (line.TrimStart().StartsWith("```"))
			{
				inBlock = !inBlock;
				continue;
			}

			if (inBlock) yield return (line, number);
		}
	}
}
