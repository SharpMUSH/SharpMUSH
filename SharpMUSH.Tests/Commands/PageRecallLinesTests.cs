using SharpMUSH.Implementation.Commands.PageCommand;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// How many lines <c>page/recall</c> and <c>pagerecall()</c> show: 10 by default, at most 500 (the portal's
/// recall cap), 0 for as many as allowed, and anything that is not a whole number refused.
/// </summary>
public class PageRecallLinesTests
{
	[Test]
	[Arguments("", 10)]
	[Arguments("  ", 10)]
	[Arguments("3", 3)]
	[Arguments("500", 500)]
	[Arguments("501", 500)]
	[Arguments("100000", 500)]
	[Arguments("0", 500)]
	public async Task ACount_IsTakenAndCapped(string text, int expected)
	{
		await Assert.That(PageRecall.TryParseLines(text, out var lines)).IsTrue();
		await Assert.That(lines).IsEqualTo(expected);
	}

	[Test]
	[Arguments("abc")]
	[Arguments("-1")]
	[Arguments("1.5")]
	public async Task ACountThatIsNotAWholeNumber_IsRefused(string text)
	{
		await Assert.That(PageRecall.TryParseLines(text, out _)).IsFalse();
	}
}
