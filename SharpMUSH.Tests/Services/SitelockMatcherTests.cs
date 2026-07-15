using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Pure unit tests for <see cref="SitelockMatcher"/> — no database, no DI container. Covers the
/// glob-on-host, CIDR-on-ip, bare-ip, and glob-on-ip branches, plus IPv6 CIDR and null/empty-arg
/// guards.
/// </summary>
public class SitelockMatcherTests
{
	[Test]
	[Arguments("*.evil.com", "1.2.3.4", "x.evil.com", true)] // glob matches host
	[Arguments("203.0.113.0/24", "203.0.113.7", "h", true)] // IPv4 CIDR contains ip
	[Arguments("203.0.113.0/24", "203.0.114.7", "h", false)] // IPv4 CIDR does not contain ip
	[Arguments("203.0.113.7", "203.0.113.7", "h", true)] // bare ip exact match
	[Arguments("10.*", "10.9.9.9", "h", true)] // glob matches ip string
	[Arguments("*.good.com", "1.2.3.4", "x.evil.com", false)] // glob matches neither host nor ip
	public async Task Matches_TableCases(string rulePattern, string ip, string host, bool expected)
	{
		var result = SitelockMatcher.Matches(rulePattern, ip, host);

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("2001:db8::/32", "2001:db8::1", "h", true)] // IPv6 CIDR contains ip
	[Arguments("2001:db8::/32", "2001:db9::1", "h", false)] // IPv6 CIDR does not contain ip
	[Arguments("::1", "::1", "h", true)] // bare IPv6 exact match
	[Arguments("::1", "::2", "h", false)] // bare IPv6 mismatch
	public async Task Matches_IPv6Cases(string rulePattern, string ip, string host, bool expected)
	{
		var result = SitelockMatcher.Matches(rulePattern, ip, host);

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	public async Task Matches_BareIpMismatch_ReturnsFalse()
	{
		var result = SitelockMatcher.Matches("203.0.113.7", "203.0.113.8", "h");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task Matches_CaseInsensitiveGlobOnHost()
	{
		var result = SitelockMatcher.Matches("*.EVIL.com", "1.2.3.4", "x.evil.COM");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task Matches_QuestionMarkGlobOnIp()
	{
		var result = SitelockMatcher.Matches("10.0.0.?", "10.0.0.5", "h");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task Matches_NullRulePattern_ReturnsFalseWithoutThrowing()
	{
		var result = SitelockMatcher.Matches(null!, "1.2.3.4", "host");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task Matches_EmptyRulePattern_ReturnsFalse()
	{
		var result = SitelockMatcher.Matches("", "1.2.3.4", "host");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task Matches_NullIp_DoesNotThrow_FallsBackToHostGlob()
	{
		var result = SitelockMatcher.Matches("*.evil.com", null!, "x.evil.com");

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task Matches_NullIpAndNoHostMatch_ReturnsFalseWithoutThrowing()
	{
		var result = SitelockMatcher.Matches("203.0.113.0/24", null!, "h");

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task Matches_NullHost_DoesNotThrow_FallsBackToIpMatch()
	{
		var result = SitelockMatcher.Matches("203.0.113.0/24", "203.0.113.7", null!);

		await Assert.That(result).IsTrue();
	}

	[Test]
	public async Task Matches_EmptyIpAndHost_ReturnsFalse()
	{
		var result = SitelockMatcher.Matches("*", "", "");

		await Assert.That(result).IsFalse();
	}
}
