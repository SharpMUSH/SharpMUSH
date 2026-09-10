using MushCommands = SharpMUSH.Implementation.Commands.Commands;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@sql/prepare</c> and <c>@mapsql/prepare</c> take <c>query,param,...</c>: a backslash escapes
/// the character after it, which is how a literal comma reaches the query.
/// </summary>
public class PreparedInputSplitTests
{
	[Test]
	[Arguments("SELECT 1", "SELECT 1")]
	[Arguments(" SELECT ? , 1 , two ", "SELECT ?|1|two")]
	[Arguments(@"SELECT 'a\,b',1", "SELECT 'a,b'|1")]
	[Arguments(@"a\\,b", @"a\|b")]
	[Arguments(@"a\\\,b", @"a\,b")]
	[Arguments(@"a\xb", "axb")]
	[Arguments(@"a\", "a")]
	[Arguments("a,,b", "a||b")]
	[Arguments("a,", "a|")]
	[Arguments("", "")]
	public async Task SplitsOnUnescapedCommas(string input, string expectedParts)
	{
		var parts = MushCommands.SplitPreparedInput(input);
		await Assert.That(string.Join('|', parts)).IsEqualTo(expectedParts);
	}
}
