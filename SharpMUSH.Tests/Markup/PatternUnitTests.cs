namespace SharpMUSH.Tests.Markup;

public class PatternUnitTests
{
	[Test]
	[Arguments("*", "(?s)^(.*?)$")]
	[Arguments("abc*def", @"(?s)^abc(.*?)def$")]
	[Arguments("abc?efg*xyz", @"(?s)^abc(.)efg(.*?)xyz$")]
	[Arguments(@"abc\?efg*xyz", @"(?s)^abc\?efg(.*?)xyz$")]
	[Arguments(@"abc\\?efg*xyz", @"(?s)^abc\\\?efg(.*?)xyz$")]
	public async Task TestWildcardAsRegex(string wildcardPattern, string expectedRegex)
	{
		var result = MModule.getWildcardMatchAsRegex(MModule.single(wildcardPattern));
		await Assert
			.That(result)
			.IsEqualTo(expectedRegex);
	}

	/// <summary>
	/// A wildcard spans newlines. PennMUSH's matcher (src/wild.c) walks characters and has no notion
	/// of a line, so <c>*</c> matches a newline like any other character — and SharpMUSH's did not,
	/// because <c>*</c> becomes <c>.</c> and .NET excludes <c>\n</c> from <c>.</c> unless told
	/// otherwise. The visible cost was any multi-line input reaching a <c>$</c>-command: a two-line
	/// pose sent to <c>$+scene/emit *=*</c> matched nothing at all and came back "Huh?", which is
	/// also why a portal compose box could not send one.
	/// </summary>
	[Test]
	[Arguments("one\ntwo", "*", true)]
	[Arguments("1=one\ntwo", "*=*", true)]
	[Arguments("head\nmiddle\ntail", "head*tail", true)]
	[Arguments("a\nb", "a?b", true)]
	public async Task TestWildcardSpansNewlines(string input, string pattern, bool expectedResult)
	{
		var result = MModule.isWildcardMatch(MModule.single(input), MModule.single(pattern));
		await Assert
			.That(result)
			.IsEqualTo(expectedResult);
	}

	/// <summary>The captured group carries the newline through, so the match is usable, not just true.</summary>
	[Test]
	public async Task TestWildcardCaptureKeepsTheNewline()
	{
		var result = MModule.getWildcardMatches(MModule.single("say one\ntwo"), MModule.single("say *"));

		await Assert
			.That(result.First().Item2.Skip(1).First().ToString())
			.IsEqualTo("one\ntwo");
	}

	[Test]
	[Arguments("abc", "*", true)]
	[Arguments("abcdefghi", "abc*ghi", true)]
	[Arguments("abcdefghi", "abc\\*ghi", false)]
	[Arguments("abc*ghi", "abc\\*ghi", true)]
	public async Task TestWildcardIsMatch(string input, string pattern, bool expectedResult)
	{
		var result = MModule.isWildcardMatch(MModule.single(input), MModule.single(pattern));
		await Assert
			.That(result)
			.IsEqualTo(expectedResult);
	}


	[Test]
	[Arguments("abc", "*", "abc")]
	[Arguments("abcdefghi", "abc*ghi", "abcdefghi")]
	[Arguments("abc*ghi", "abc\\*ghi", "abc*ghi")]
	public async Task TestWildcardMatch(string input, string pattern, string expectedResult)
	{
		var result = MModule.getWildcardMatches(MModule.single(input), MModule.single(pattern)).First();
		await Assert
			.That(result.Item2.First().ToString())
			.IsEqualTo(expectedResult);
	}

	[Test]
	[Arguments("abc", "*", "abc")]
	[Arguments("abcdefghi", "abc*ghi", "abcdefghi")]
	[Arguments("abc*ghi", "abc\\*ghi", "abc*ghi")]
	public async Task TestWildcardMatches(string input, string pattern, string expectedResult)
	{
		var result = MModule.getWildcardMatches(MModule.single(input), MModule.single(pattern));
		await Assert
			.That(result.First().Item2.First().ToString())
			.IsEqualTo(expectedResult);
	}

	[Test]
	[Arguments("abc", "*", "abc")]
	[Arguments("abcdefghi", "abc*ghi", "def")]
	[Arguments("abc*ghi", "abc\\*ghi", null)]
	public async Task TestWildcardMatches2(string input, string pattern, string? expectedResult)
	{
		var result = MModule.getWildcardMatches(MModule.single(input), MModule.single(pattern));
		await Assert
			.That(result.First().Item2.Skip(1).FirstOrDefault()?.ToString())
			.IsEqualTo(expectedResult);
	}
}