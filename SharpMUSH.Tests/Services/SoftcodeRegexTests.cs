using SharpMUSH.Library.Utilities;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Patterns that arrive from softcode — <c>regmatch()</c>, <c>@grep</c>, a <c>^</c>-listen, a wildcard
/// <c>lattr</c> — are written by anyone who can build. The engine runs its command queue one entry at
/// a time, so a pattern that backtracks catastrophically does not slow one command down, it stops the
/// game. Every such pattern is built here, with a bound on how long a single match may take.
/// </summary>
public class SoftcodeRegexTests
{
	// (a+)+$ against a long run of 'a' with no terminator: the classic exponential backtrack.
	private const string Catastrophic = "(a+)+$";
	private static readonly string Adversarial = new string('a', 40) + "!";

	[Test]
	public async Task APatternThatCannotFinishInTimeStopsInsteadOfHangingTheGame()
	{
		var regex = SoftcodeRegex.Create(Catastrophic, RegexOptions.None);

		await Assert.That(() => regex.IsMatch(Adversarial)).Throws<RegexMatchTimeoutException>();
	}

	[Test]
	public async Task TheBoundIsShortEnoughToNotBeTheHang()
		=> await Assert.That(SoftcodeRegex.MatchTimeout).IsLessThanOrEqualTo(TimeSpan.FromMilliseconds(250));

	[Test]
	public async Task AnOrdinaryPatternStillWorks()
	{
		var regex = SoftcodeRegex.Create("^h(.)llo (.*)$", RegexOptions.IgnoreCase);

		var match = regex.Match("Hello World");

		await Assert.That(match.Success).IsTrue();
		await Assert.That(match.Groups[2].Value).IsEqualTo("World");
	}

	[Test]
	public async Task AnInvalidPatternIsReportedAsInvalidRatherThanThrowingSomethingElse()
		=> await Assert.That(() => SoftcodeRegex.Create("(unclosed", RegexOptions.None))
			.Throws<ArgumentException>();

	/// <summary>
	/// The same pattern text asked for twice hands back the same instance: a wildcard <c>lattr</c> or a
	/// LISTEN pattern is recompiled on every message otherwise, and construction is the expensive part.
	/// </summary>
	[Test]
	public async Task TheSamePatternIsOnlyBuiltOnce()
	{
		var first = SoftcodeRegex.Create("^cached (.*)$", RegexOptions.IgnoreCase);
		var second = SoftcodeRegex.Create("^cached (.*)$", RegexOptions.IgnoreCase);

		await Assert.That(ReferenceEquals(first, second)).IsTrue();
	}

	[Test]
	public async Task PatternsThatDifferOnlyInOptionsAreNotShared()
	{
		var sensitive = SoftcodeRegex.Create("^opts (.*)$", RegexOptions.None);
		var insensitive = SoftcodeRegex.Create("^opts (.*)$", RegexOptions.IgnoreCase);

		await Assert.That(ReferenceEquals(sensitive, insensitive)).IsFalse();
		await Assert.That(insensitive.IsMatch("OPTS x")).IsTrue();
		await Assert.That(sensitive.IsMatch("OPTS x")).IsFalse();
	}
}
