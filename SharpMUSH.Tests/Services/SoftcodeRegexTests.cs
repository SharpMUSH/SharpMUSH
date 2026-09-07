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

	/// <summary>
	/// The bound exists because the pattern text is player-supplied. It is a least-recently-used bound,
	/// not a stop-admitting one: a cache that refuses everything once full would let the first thousand
	/// patterns a server saw lock out every pattern after them, including the ones actually being used.
	/// So this both fills it past capacity and then checks a fresh pattern still gets in.
	/// </summary>
	[Test]
	public async Task ConcurrentMissesDoNotPushTheCacheOverItsBound()
	{
		await Task.WhenAll(Enumerable.Range(0, Environment.ProcessorCount * 4).Select(worker =>
			Task.Run(() =>
			{
				for (var i = 0; i < 600; i++)
				{
					SoftcodeRegex.Create($"^bound{worker}x{i}$", RegexOptions.None);
				}
			})));

		await Assert.That(SoftcodeRegex.CachedCount).IsLessThanOrEqualTo(SoftcodeRegex.Capacity);

		// And a pattern arriving after saturation is still admitted, or the cache is useless on any
		// server that has been up long enough to see a thousand of them. Eventually, not instantly:
		// MemoryCache compacts on a background thread, and until it has caught up with a burst this
		// size a Set is simply dropped. Retried rather than slept on, and bounded so it can still fail.
		var readmitted = false;
		for (var attempt = 0; attempt < 100 && !readmitted; attempt++)
		{
			var first = SoftcodeRegex.Create("^admitted after the flood$", RegexOptions.None);
			readmitted = ReferenceEquals(first,
				SoftcodeRegex.Create("^admitted after the flood$", RegexOptions.None));
			if (!readmitted)
			{
				await Task.Delay(20);
			}
		}

		await Assert.That(readmitted).IsTrue()
			.Because("a bound that stops admitting for good would lock out the patterns actually in use");
	}

	/// <summary>
	/// The timeout turned a hang into a thrown exception, which is only an improvement where somebody
	/// catches it. On the engine's own matching paths — command discovery, listen patterns, sitelock —
	/// there is no answer to give a player, so the rule is that a pattern which cannot finish does not
	/// match, and it is applied here rather than at each of those call sites.
	/// </summary>
	[Test]
	public async Task APatternThatTimesOutDoesNotMatch()
	{
		var regex = SoftcodeRegex.Create(Catastrophic, RegexOptions.None);

		await Assert.That(SoftcodeRegex.IsMatch(regex, Adversarial)).IsFalse();
	}

	[Test]
	public async Task APatternThatTimesOutProducesNoMatch()
	{
		var regex = SoftcodeRegex.Create(Catastrophic, RegexOptions.None);

		await Assert.That(SoftcodeRegex.Match(regex, Adversarial)).IsNull();
	}

	[Test]
	public async Task AnOrdinaryPatternStillMatchesThroughTheGuardedForms()
	{
		var regex = SoftcodeRegex.Create("^h(.)llo (.*)$", RegexOptions.IgnoreCase);

		await Assert.That(SoftcodeRegex.IsMatch(regex, "Hello World")).IsTrue();
		await Assert.That(SoftcodeRegex.Match(regex, "Hello World")!.Groups[2].Value).IsEqualTo("World");
		await Assert.That(SoftcodeRegex.Match(regex, "nope")!.Success).IsFalse()
			.Because("not matching is an answer; only a pattern that could not finish has none");
	}
}
