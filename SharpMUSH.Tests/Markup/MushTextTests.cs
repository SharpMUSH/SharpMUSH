using System.Diagnostics;
using System.Drawing;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using M = MarkupString.Ansi.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Covers the MUSH-specific text operations: PennMUSH's list-splitting rule, space compression,
/// and the glob matcher.
/// </summary>
public class MushTextTests
{
	// ── SplitList ────────────────────────────────────────────────────────────────

	[Test]
	public async Task SplitList_OnSpace_DropsEmptyItems()
	{
		var items = MushText.SplitList(MarkupText.Space, MarkupText.Plain("a  b   c"));

		await Assert.That(items.Select(x => x.ToPlainText())).IsEquivalentTo(["a", "b", "c"]);
	}

	[Test]
	public async Task SplitList_OnSpace_DropsLeadingAndTrailingEmpties()
	{
		var items = MushText.SplitList(MarkupText.Space, MarkupText.Plain("  a b  "));

		await Assert.That(items.Select(x => x.ToPlainText())).IsEquivalentTo(["a", "b"]);
	}

	[Test]
	public async Task SplitList_OnOtherDelimiter_KeepsEmptyItems()
	{
		var items = MushText.SplitList(MarkupText.Plain("|"), MarkupText.Plain("a||b"));

		await Assert.That(items.Select(x => x.ToPlainText())).IsEquivalentTo(["a", "", "b"]);
	}

	[Test]
	public async Task SplitList_KeepsTheMarkupOfEachItem()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		var text = MarkupText.Concat(MarkupText.Wrap(red, "red"), MarkupText.Plain(" plain"));

		var items = MushText.SplitList(MarkupText.Space, text);

		await Assert.That(items.Length).IsEqualTo(2);
		await Assert.That(items[0].Runs.Length).IsEqualTo(1);
		await Assert.That(items[0].Runs[0].Markups[0]).IsEqualTo(red);
		await Assert.That(items[1].Runs.Length).IsEqualTo(0);
	}

	// ── CompressSpaces ───────────────────────────────────────────────────────────

	[Test]
	[Arguments("a  b", "a b")]
	[Arguments("a   b    c", "a b c")]
	[Arguments("   leading", " leading")]
	[Arguments("trailing   ", "trailing ")]
	[Arguments("a b c", "a b c")]
	[Arguments("", "")]
	public async Task CompressSpaces_CollapsesRunsToOne(string input, string expected)
		=> await Assert.That(MushText.CompressSpaces(MarkupText.Plain(input)).ToPlainText()).IsEqualTo(expected);

	[Test]
	public async Task CompressSpaces_WithNothingToDo_ReturnsTheSameInstance()
	{
		var text = MarkupText.Plain("nothing to compress here");

		await Assert.That(ReferenceEquals(MushText.CompressSpaces(text), text)).IsTrue();
	}

	[Test]
	public async Task CompressSpaces_KeepsTheMarkupOnTheSurvivingText()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		var text = MarkupText.Concat(
			MarkupText.Wrap(red, "red"),
			MarkupText.Plain("     tail"));

		var compressed = MushText.CompressSpaces(text);

		await Assert.That(compressed.ToPlainText()).IsEqualTo("red tail");
		await Assert.That(compressed.Runs.Length).IsEqualTo(1);
		await Assert.That(compressed.Runs[0].Start).IsEqualTo(0);
		await Assert.That(compressed.Runs[0].Length).IsEqualTo(3);
		await Assert.That(compressed.Runs[0].Markups[0]).IsEqualTo(red);
	}

	/// <summary>
	/// Compression is one scan and one <c>Splice</c>, so a run of spaces costs what its length says
	/// and not its square. 64k spaces is far past the point where a per-run re-splice stops
	/// finishing at all.
	/// </summary>
	[Test]
	public async Task CompressSpaces_OnAPathologicalInput_IsLinear()
	{
		var input = MarkupText.Plain("a" + new string(' ', 65_536) + "b");

		// Warm the JIT so the measured pass is not paying for first-call compilation.
		MushText.CompressSpaces(MarkupText.Plain("x  y"));

		var stopwatch = Stopwatch.StartNew();
		var compressed = MushText.CompressSpaces(input);
		stopwatch.Stop();

		await Assert.That(compressed.ToPlainText()).IsEqualTo("a b");
		await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(20);
	}

	[Test]
	public async Task CompressSpaces_ManyRuns_CollapsesEachOfThem()
	{
		var input = MarkupText.Plain(string.Join("   ", Enumerable.Range(0, 1_000).Select(i => i.ToString())));

		var compressed = MushText.CompressSpaces(input);

		await Assert.That(compressed.ToPlainText())
			.IsEqualTo(string.Join(" ", Enumerable.Range(0, 1_000).Select(i => i.ToString())));
	}

	// ── Glob ─────────────────────────────────────────────────────────────────────

	[Test]
	[Arguments("*", "anything at all", true)]
	[Arguments("he*o", "hello", true)]
	[Arguments("he*o", "helicopter", false)]
	[Arguments("h?llo", "hello", true)]
	[Arguments("h?llo", "heello", false)]
	[Arguments("plain", "plain", true)]
	[Arguments("plain", "plainer", false)]
	// A regex metacharacter in the pattern is literal text, not syntax.
	[Arguments("a.b", "a.b", true)]
	[Arguments("a.b", "axb", false)]
	public async Task IsWildcardMatch_MatchesTheGlob(string pattern, string input, bool expected)
		=> await Assert.That(MushText.IsWildcardMatch(MarkupText.Plain(input), pattern)).IsEqualTo(expected);

	/// <summary>
	/// PennMUSH's matcher walks characters and has no notion of a line, so <c>*</c> spans a newline.
	/// .NET excludes <c>\n</c> from <c>.</c> unless the pattern asks for single-line mode.
	/// </summary>
	[Test]
	public async Task IsWildcardMatch_SpansANewline()
		=> await Assert.That(MushText.IsWildcardMatch(MarkupText.Plain("first\nsecond"), "first*second")).IsTrue();

	[Test]
	public async Task Glob_ToRegex_EscapesTheLiteralWildcards()
	{
		// `\*` in the pattern is a literal asterisk, so it must not become a capture group.
		await Assert.That(MushText.IsWildcardMatch(MarkupText.Plain("a*b"), @"a\*b")).IsTrue();
		await Assert.That(MushText.IsWildcardMatch(MarkupText.Plain("axb"), @"a\*b")).IsFalse();
	}

	/// <summary>
	/// Exact-output coverage for the glob-to-regex compiler, ported from the deleted
	/// <c>PatternUnitTests.TestWildcardAsRegex</c>. The regex text itself is the contract: it is
	/// handed to callers (<c>CommandAttributeScanner</c> compiles <c>$</c>-command patterns from it)
	/// that rely on the exact capture-group shape, not just on whether a given input matches.
	/// </summary>
	[Test]
	[Arguments("*", "(?s)^(.*?)$")]
	[Arguments("abc*def", @"(?s)^abc(.*?)def$")]
	[Arguments("abc?efg*xyz", @"(?s)^abc(.)efg(.*?)xyz$")]
	[Arguments(@"abc\?efg*xyz", @"(?s)^abc\?efg(.*?)xyz$")]
	[Arguments(@"abc\\?efg*xyz", @"(?s)^abc\\\?efg(.*?)xyz$")]
	public async Task Glob_ToRegex_ProducesTheExpectedPattern(string wildcardPattern, string expectedRegex)
		=> await Assert.That(MushText.Glob.ToRegex(wildcardPattern)).IsEqualTo(expectedRegex);

	[Test]
	[Arguments("*")]
	[Arguments("abc*def")]
	[Arguments("abc?efg*xyz")]
	[Arguments(@"abc\?efg*xyz")]
	[Arguments(@"abc\\?efg*xyz")]
	public async Task Glob_ToRegex_AlwaysOpensWithSingleLineMode(string wildcardPattern)
		=> await Assert.That(MushText.Glob.ToRegex(wildcardPattern)).StartsWith("(?s)");

	[Test]
	public async Task GetWildcardMatches_ReturnsGroupsCarryingTheirMarkup()
	{
		var red = M.Create(foreground: Color.Red.ToAnsiColor());
		var input = MarkupText.Concat(MarkupText.Plain("say "), MarkupText.Wrap(red, "hello"));

		var groups = MushText.GetWildcardMatches(input, MarkupText.Plain("say *"))
			.Single().Groups.ToArray();

		await Assert.That(groups[1].ToPlainText()).IsEqualTo("hello");
		await Assert.That(groups[1].Runs.Length).IsEqualTo(1);
		await Assert.That(groups[1].Runs[0].Markups[0]).IsEqualTo(red);
	}

	[Test]
	public async Task GetRegexpMatches_ReturnsEveryMatch()
	{
		var matches = MushText.GetRegexpMatches(MarkupText.Plain("a1 b2 c3"), MarkupText.Plain(@"([a-z])(\d)"))
			.ToArray();

		await Assert.That(matches.Length).IsEqualTo(3);
		await Assert.That(matches.Select(m => m.Groups.ElementAt(1).ToPlainText())).IsEquivalentTo(["a", "b", "c"]);
	}

	// ── Singletons ───────────────────────────────────────────────────────────────

	[Test]
	public async Task Singletons_CarryTheExpectedText()
	{
		await Assert.That(MushText.Error.ToPlainText()).IsEqualTo("#-1");
		await Assert.That(MushText.Zero.ToPlainText()).IsEqualTo("0");
		await Assert.That(MushText.One.ToPlainText()).IsEqualTo("1");
		await Assert.That(MushText.Comma.ToPlainText()).IsEqualTo(",");
	}
}
