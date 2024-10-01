namespace SharpMUSH.Tests.Markup;

public class PatternUnitTests
{
	[Test]
	[Arguments("*", "^(.*?)$")]
	[Arguments("abc*def", @"^abc(.*?)def$")]
	[Arguments("abc?efg*xyz", @"^abc(.)efg(.*?)xyz$")]
	[Arguments(@"abc\?efg*xyz", @"^abc\?efg(.*?)xyz$")]
	[Arguments(@"abc\\?efg*xyz", @"^abc\\\?efg(.*?)xyz$")]
	public async Task TestWildcardAsRegex(string wildcardPattern, string expectedRegex)
	{
		var result = MModule.getWildcardMatchAsRegex(MModule.single(wildcardPattern));
		await Assert
			.That(result)
			.IsEqualTo(expectedRegex);
	}

	[Test]
	[Arguments("abc", "*", true)]
	[Arguments("abcdefghi", "abc*ghi", true)]
	[Arguments("abcdefghi", "abc\\*ghi", false)]
	[Arguments("abc*ghi", "abc\\*ghi", true)]
	public async Task TestWildcardMatch(string input, string pattern, bool expectedResult)
	{
		var result = MModule.isWildcardMatch(MModule.single(input), MModule.single(pattern));
		await Assert
			.That(result)
			.IsEqualTo(expectedResult);
	}

	
}