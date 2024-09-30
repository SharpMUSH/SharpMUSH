namespace SharpMUSH.Tests.Markup;

public class PatternUnitTests
{
	[Test]
	[Arguments("*", "^(.*?)$")]
	[Arguments("abc*def", @"^abc(.*?)def$")]
	[Arguments("abc?efg*xyz", @"^abc(.)efg(.*?)xyz$")]
	[Arguments(@"abc\?efg*xyz", @"^abc\?efg(.*?)xyz$")]
	[Arguments(@"abc\\?efg*xyz", @"^abc\\\\(.)efg(.*?)xyz$")]
	// [Arguments(@"abc\\\?efg*xyz", @"^abc\\\\\?efg(.*?)xyz$")]
	// [Arguments(@"abc\\\\?efg*xyz", @"^abc\\\\\\\\(.)efg(.*?)xyz$")]
	public async Task TestWildcard(string wildcardPattern, string expectedRegex)
	{
		var result = MModule.getWildcardMatchAsRegex(MModule.single(wildcardPattern));
		await Assert
			.That(result)
			.IsEqualTo(expectedRegex);
	}
}