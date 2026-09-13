using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Services;

public class IncomingFilterPatternTests
{
	[Test]
	[Arguments("(a,b),tail", "(a,b)|tail")]
	[Arguments("{a,b},[c,d],tail", "{a,b}|[c,d]|tail")]
	[Arguments(@"a\,b,tail", @"a\,b|tail")]
	[Arguments(@"a\\,tail", @"a\\|tail")]
	[Arguments("%,,tail", "%,|tail")]
	[Arguments("%q,,tail", "%q,|tail")]
	[Arguments("%v,,tail", "%v,|tail")]
	[Arguments("%w,,tail", "%w,|tail")]
	[Arguments("%x,,tail", "%x,|tail")]
	[Arguments("%q<a,b>,tail", "%q<a,b>|tail")]
	[Arguments("%q<(a,b),c>,tail", "%q<(a,b),c>|tail")]
	[Arguments(" a , b ", " a | b ")]
	[Arguments(",,a,,", "a")]
	[Arguments("([a,b),tail", "([a,b),tail")]
	[Arguments("unclosed%,", "unclosed%,")]
	[Arguments("%q<unclosed,a,b", "%q<unclosed,a,b")]
	[Arguments("", "")]
	public async Task CommasFollowUnevaluatedGroupingAndEscapeRules(string input, string expected)
		=> await Assert.That(string.Join('|', IncomingFilterPatterns.Split(input))).IsEqualTo(expected);

	[Test]
	[Arguments(">5", "6", true)]
	[Arguments(">=5", "5", true)]
	[Arguments("<5", "4", true)]
	[Arguments("<=5", "5", true)]
	[Arguments(">5", "5", false)]
	[Arguments("<5", "5", false)]
	[Arguments(">alpha", "beta", true)]
	[Arguments("<=beta", "alpha", true)]
	[Arguments(">=NaN", "NaN", false)]
	[Arguments("<=NaN", "NaN", false)]
	[Arguments(">NaN", "5", false)]
	[Arguments("<NaN", "5", false)]
	[Arguments(">=Infinity", "Infinity", true)]
	[Arguments(">1e999", "2", true)]
	[Arguments(">=1e-999", "0", false)]
	[Arguments(">=0x1p-1074", "0x1p-1074", true)]
	[Arguments(">=0e-999", "0", true)]
	[Arguments(">=-0x0p-9999", "0", true)]
	[Arguments(@"\>5", ">5", true)]
	public async Task OrderingPrefixesBelongToNonRegexFilters(string pattern, string input, bool expected)
		=> await Assert.That(IncomingFilterPatterns.Matches(pattern, input, false, false)).IsEqualTo(expected);

	[Test]
	public async Task RegexCaseAndCompatibilityRemainExplicit()
	{
		await Assert.That(IncomingFilterPatterns.Matches(">5", ">5", true, false)).IsTrue();
		await Assert.That(IncomingFilterPatterns.Matches(">5", "6", true, false)).IsFalse();
		await Assert.That(IncomingFilterPatterns.Matches("UPPER", "upper", false, true)).IsFalse();
		await Assert.That(IncomingFilterPatterns.Matches("UPPER", "upper", false, false)).IsTrue();
		await Assert.That(IncomingFilterPatterns.Matches(">5", "6suffix", false, false, new(true, false))).IsTrue();
		await Assert.That(IncomingFilterPatterns.Matches(">=0", "", false, false, new(false, true))).IsTrue();
	}

	[Test]
	public async Task CancellationStopsLiteralScanning()
	{
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();
		await Assert.That(() => IncomingFilterPatterns.Split("(a,b),tail", cancellation.Token).ToArray()).Throws<OperationCanceledException>();
	}
}
