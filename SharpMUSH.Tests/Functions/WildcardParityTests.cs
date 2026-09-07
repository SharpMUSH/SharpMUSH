using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Utilities;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// What a wildcard pattern means, against PennMUSH's <c>wild_match_test</c> (<c>src/wild.c:234</c>),
/// which is the definition every <c>$</c>-command, <c>@listen</c>, <c>grab()</c> and <c>@grep</c>
/// pattern in the game is measured by.
/// </summary>
/// <remarks>
/// Four things that source says and the engine did not: the match is whole-string, it is
/// case-insensitive, a run of <c>*</c> collapses to one glob while still occupying a capture register
/// per star, and <c>\</c> makes the next character literal. The fifth is not in the source at all —
/// PennMUSH's matcher is a linear scan, so no pattern can make it take seconds.
/// </remarks>
public class WildcardParityTests
{
	private static bool Matches(string glob, string subject)
		=> SoftcodeRegex.IsMatch(SoftcodeRegex.Wildcard(glob), subject);

	private static string[] Captures(string glob, string subject)
	{
		var match = SoftcodeRegex.Wildcard(glob).Match(subject);
		return match.Success ? [.. match.Groups.Values.Skip(1).Select(g => g.Value)] : [];
	}

	/// <summary>
	/// <c>wild_match_test</c> consumes the whole subject and returns <c>!pat[pbase + pi]</c>: the
	/// pattern has to be spent when the subject is. A substring hit is not a match.
	/// </summary>
	[Test]
	[Arguments("tes*", "test", true)]
	[Arguments("tes*", "testing", true)]
	[Arguments("tes*", "attest", false)]
	[Arguments("ab", "ab", true)]
	[Arguments("ab", "xxabxx", false)]
	[Arguments("*test", "a test", true)]
	[Arguments("*test", "a testing", false)]
	[Arguments("*", "", true)]
	public async Task AWildcardMatchIsWholeString(string glob, string subject, bool expected)
		=> await Assert.That(Matches(glob, subject)).IsEqualTo(expected);

	/// <summary><c>quick_wild</c> passes <c>cs = 0</c>, and <c>wild_match_test</c> upcases both sides.</summary>
	[Test]
	[Arguments("TES*", "test")]
	[Arguments("tes*", "TEST")]
	[Arguments("Test", "tESt")]
	public async Task AWildcardMatchIsCaseInsensitive(string glob, string subject)
		=> await Assert.That(Matches(glob, subject)).IsTrue();

	/// <summary>
	/// <c>case '*'</c> skips past consecutive globs in one loop, but takes a capture register for each
	/// one it skips, and only the last of the run grows. So <c>a**b</c> has two registers and the first
	/// is empty — <c>%0</c> and <c>%1</c> have to keep meaning what they meant.
	/// </summary>
	[Test]
	public async Task ARunOfStarsKeepsOneRegisterPerStarAndFillsOnlyTheLast()
	{
		await Assert.That(Captures("a*b", "axyzb")).IsEquivalentTo(new[] { "xyz" });
		await Assert.That(Captures("a**b", "axyzb")).IsEquivalentTo(new[] { "", "xyz" });
		await Assert.That(Captures("a***b", "axyzb")).IsEquivalentTo(new[] { "", "", "xyz" });
	}

	[Test]
	public async Task QuestionMarkTakesExactlyOneCharacterAndCapturesIt()
	{
		await Assert.That(Matches("?x", "qx")).IsTrue();
		await Assert.That(Matches("?x", "x")).IsFalse().Because("? has to consume a character");
		await Assert.That(Captures("?x*", "qxyz")).IsEquivalentTo(new[] { "q", "yz" });
	}

	/// <summary><c>case '\\'</c>: "Literal match of the next character, which may be a * or ?".</summary>
	[Test]
	public async Task ABackslashMakesTheNextCharacterLiteral()
	{
		await Assert.That(Matches(@"a\*b", "a*b")).IsTrue();
		await Assert.That(Matches(@"a\*b", "axb")).IsFalse();
		await Assert.That(Matches(@"a\?b", "a?b")).IsTrue();
		await Assert.That(Matches(@"a\?b", "axb")).IsFalse();
	}

	[Test]
	public async Task ATrailingStarMatchesTheRestIncludingNothing()
	{
		await Assert.That(Matches("*", "")).IsTrue();
		await Assert.That(Matches("*", "anything at all")).IsTrue();
		await Assert.That(Matches("a*", "a")).IsTrue();
	}

	/// <summary>
	/// PennMUSH's matcher is a linear scan with no backtracking, so no pattern makes it slow. Ours
	/// builds a regex, and a run of stars used to become nested lazy quantifiers: six stars took 2.3
	/// seconds against a sixty-character subject, seven took longer than five. On a queue that runs one
	/// command at a time that is the whole game stopped, by a pattern any builder could set.
	/// </summary>
	[Test]
	[Arguments("******b")]
	[Arguments("**********b")]
	[Arguments("*a*a*a*a*a*a*b")]
	[Arguments("*a*b*c*d*e*f*g*")]
	public async Task NoWildcardTakesLongerThanAnEyeblink(string glob)
	{
		var adversarial = new string('a', 60) + "!";

		var stopwatch = Stopwatch.StartNew();
		var matched = Matches(glob, adversarial);
		stopwatch.Stop();

		await Assert.That(stopwatch.ElapsedMilliseconds).IsLessThan(50)
			.Because($"'{glob}' should be answered by a scan, not a search");
		await Assert.That(matched).IsFalse();
	}

	/// <summary>
	/// The list functions had a transformation of their own — unanchored, so <c>grab(list, ab)</c>
	/// matched an element <c>xxabxx</c>. Both spellings are one now.
	/// </summary>
	[Test]
	public async Task TheListFunctionTransformationIsTheSameOne()
	{
		await Assert.That("tes*".GlobToRegex()).IsEqualTo(MushText.Glob.ToRegex("tes*"));
		await Assert.That(Regex.IsMatch("attest", "tes*".GlobToRegex())).IsFalse();
	}

	/// <summary>
	/// Case is an argument to <c>wild_match_test</c>, not a property of the pattern: <c>quick_wild</c>
	/// passes 0 and <c>grep_util</c> passes 1 unless the caller used the "i" variant. So the translated
	/// pattern folds nothing, and the choice stays with the caller — <c>wildgrep</c> needs to be able to
	/// make it, and it could not if the pattern had already decided.
	/// </summary>
	[Test]
	public async Task CaseIsTheCallersChoiceAndDefaultsToInsensitive()
	{
		await Assert.That(MushText.Glob.ToRegex("tes*")).DoesNotContain("(?i");

		await Assert.That(SoftcodeRegex.Wildcard("tes*").IsMatch("TEST")).IsTrue()
			.Because("quick_wild passes cs = 0, and $-commands, @listen and grab() all go through it");
		await Assert.That(SoftcodeRegex.Wildcard("tes*", caseSensitive: true).IsMatch("TEST")).IsFalse()
			.Because("wildgrep passes cs = 1");
		await Assert.That(SoftcodeRegex.Wildcard("tes*", caseSensitive: true).IsMatch("test")).IsTrue();
	}
}
