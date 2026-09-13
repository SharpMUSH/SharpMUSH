using MarkupString;
using MarkupString.Ansi;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Utilities;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Services;

public class WildcardEscapeTests
{
	[Test]
	[Arguments(@"a\b", "ab", true)]
	[Arguments(@"a\b", @"a\b", false)]
	[Arguments(@"\\", @"\", true)]
	[Arguments(@"\\*", @"\hello", true)]
	[Arguments(@"\\?", @"\x", true)]
	[Arguments(@"\*", "*", true)]
	[Arguments(@"\?", "?", true)]
	[Arguments(@"\\\*", @"\*", true)]
	[Arguments(@"\\\*", @"\hello", false)]
	[Arguments(@"\\\\*", @"\\hello", true)]
	[Arguments(@"\", @"\", false)]
	[Arguments(@"\", "", false)]
	[Arguments(@"a\", @"a\", false)]
	[Arguments(@"a\", "a", false)]
	[Arguments(@"**\", @"hello\", false)]
	[Arguments("a", "a\n", false)]
	[Arguments("", "\n", false)]
	[Arguments("", "", true)]
	[Arguments("*", "\n", true)]
	[Arguments("?", "\n", true)]
	[Arguments(@"\,", ",", true)]
	[Arguments(@"\[", "[", true)]
	public async Task EscapesAndWholeInputMatching(string pattern, string input, bool expected)
	{
		foreach (var options in new[] { RegexOptions.None, RegexOptions.NonBacktracking, RegexOptions.Multiline })
		{
			var regex = SoftcodeRegex.Wildcard(pattern, options);
			await Assert.That(regex.IsMatch(input)).IsEqualTo(expected);
			await Assert.That(regex.MatchTimeout).IsEqualTo(SoftcodeRegex.MatchTimeout);
		}
	}

	[Test]
	public async Task ActiveWildcardsKeepCaptureOrderAndMarkupAfterEscapedCharacters()
	{
		var red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));
		var input = MarkupText.Concat([MarkupText.Plain(@"ab\*"), MarkupText.Wrap(red, "first\nsecond"), MarkupText.Plain("?Z")]);
		var groups = MushText.GetWildcardMatches(input, MarkupText.Plain(@"a\b\\\**\??")).Single().Groups.ToArray();
		await Assert.That(groups.Select(group => group.ToPlainText())
			.SequenceEqual([@"ab\*first" + "\nsecond?Z", "first\nsecond", "Z"])).IsTrue();
		await Assert.That(groups[1].Runs.Single().Markups.Single()).IsEqualTo(red);
	}

	[Test]
	public async Task EscapedLettersKeepCallerCasePolicy()
	{
		await Assert.That(SoftcodeRegex.Wildcard(@"\a").IsMatch("A")).IsTrue();
		await Assert.That(SoftcodeRegex.Wildcard(@"\a", caseSensitive: true).IsMatch("A")).IsFalse();
	}
}
