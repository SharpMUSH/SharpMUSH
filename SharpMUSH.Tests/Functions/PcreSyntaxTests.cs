using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The PCRE spellings PennMUSH's patterns accept, rewritten for .NET's engine.
/// </summary>
public class PcreSyntaxTests
{
	[Test]
	[Arguments("[[:alnum:]]", "[a-zA-Z0-9]")]
	[Arguments("[^[:digit:]_]", "[^0-9_]")]
	[Arguments("[]a[:upper:]]", "[]aA-Z]")]
	[Arguments(@"\[[:alpha:]]", @"\[[:alpha:]]")]
	[Arguments("[[:nosuch:]]", "[[:nosuch:]]")]
	[Arguments("(?P<c>.)est", "(?<c>.)est")]
	[Arguments("(?P<x>ab)(?P=x)", @"(?<x>ab)\k<x>")]
	[Arguments("plain(text)", "plain(text)")]
	public async Task Rewrites(string pattern, string expected)
		=> await Assert.That(PcreSyntax.ToDotNet(pattern)).IsEqualTo(expected);

	[Test]
	[Arguments("^[[:space:]]$", " ", true)]
	[Arguments("^[[:xdigit:]]+$", "0fA9", true)]
	[Arguments("^[[:xdigit:]]+$", "g", false)]
	[Arguments("^[[:punct:]]+$", "!-[]{}~", true)]
	[Arguments("^(?P<w>a)(?P=w)$", "aa", true)]
	public async Task Matches(string pattern, string subject, bool expected)
		=> await Assert.That(SoftcodeRegex.Create(pattern, System.Text.RegularExpressions.RegexOptions.None).IsMatch(subject)).IsEqualTo(expected);
}
