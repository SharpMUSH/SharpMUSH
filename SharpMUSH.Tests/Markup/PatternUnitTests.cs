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
	public async Task TestWildcardIsMatch(string input, string pattern, bool expectedResult)
	{
		var result = MModule.isWildcardMatch(MModule.single(input), MModule.single(pattern));
		await Assert
			.That(result)
			.IsEqualTo(expectedResult);
	}
	

	[Test]
	[Arguments("abc", "*", "abc")]
	[Arguments("abcdefghi", "abc*ghi","abcdefghi")]
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
	[Arguments("abcdefghi", "abc*ghi","abcdefghi")]
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
	[Arguments("abcdefghi", "abc*ghi","def")]
	[Arguments("abc*ghi", "abc\\*ghi", null)]
	public async Task TestWildcardMatches2(string input, string pattern, string? expectedResult)
	{
		var result = MModule.getWildcardMatches(MModule.single(input), MModule.single(pattern));
		await Assert
			.That(result.First().Item2.Skip(1).FirstOrDefault()?.ToString())
			.IsEqualTo(expectedResult);
	}
}