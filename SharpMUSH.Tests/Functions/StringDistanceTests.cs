using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.Functions;

public class StringDistanceTests
{
	[Test]
	[Arguments("kitten", "sitting", 3)]
	[Arguments("cat", "cats", 1)]
	[Arguments("cats", "cat", 1)]
	[Arguments("cat", "cut", 1)]
	[Arguments("same", "same", 0)]
	[Arguments("", "", 0)]
	[Arguments("", "é😀", 2)]
	[Arguments("é😀", "", 2)]
	[Arguments("A", "a", 1)]
	[Arguments("\u00e9", "e\u0301", 1)]
	[Arguments("e\u0301", "e\u0301", 0)]
	[Arguments("😀", "😺", 1)]
	[Arguments("👩‍👩‍👧‍👦", "👨‍👩‍👦", 1)]
	[Arguments("🇺🇸", "🇺🇸🇨🇦", 1)]
	[Arguments("ab", "ba", 2)]
	public async Task CountsOrdinalGraphemeEdits(string source, string target, int expected)
	{
		await Assert.That(StringDistance.TryCalculate(MarkupText.Plain(source), MarkupText.Plain(target), out var actual)).IsTrue();
		await Assert.That(actual).IsEqualTo(expected);
	}

	[Test]
	public async Task MarkupDoesNotContributeToDistance()
	{
		var left = MarkupText.Wrap(HtmlMarkup.Create("b"), MarkupText.Wrap(AnsiMarkup.Create(underlined: true), "界😀"));
		var right = MarkupText.Wrap(HtmlMarkup.Create("i"), "界😀");
		await Assert.That(StringDistance.TryCalculate(left, right, out var distance)).IsTrue();
		await Assert.That(distance).IsEqualTo(0);
	}

	[Test]
	public async Task LongClustersAreComparedAsWholeSymbols()
	{
		var cluster = "e" + new string('\u0301', 1024);
		await Assert.That(StringDistance.TryCalculate(MarkupText.Plain(cluster), MarkupText.Plain(cluster + "x"), out var distance)).IsTrue();
		await Assert.That(distance).IsEqualTo(1);
	}

	[Test]
	public async Task CheckedWorkCeilingRejectsBeforeAllocatingRowsOrTokens()
	{
		var left = MarkupText.Plain(new string('a', 2001));
		var right = MarkupText.Plain(new string('b', 2000));
		var before = GC.GetAllocatedBytesForCurrentThread();
		var success = StringDistance.TryCalculate(left, right, out _);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		await Assert.That(success).IsFalse();
		await Assert.That(allocated).IsLessThan(10_000L);
		await Assert.That(StringDistance.TryCalculate(left.SubstringGraphemes(0, 2000), right, out var distance)).IsTrue();
		await Assert.That(distance).IsEqualTo(2000);
	}

	[Test]
	public async Task InputBoundsApplyEvenToEmptyOrIdenticalComparisons()
	{
		var many = MarkupText.Plain(new string('a', StringDistance.MaxInputGraphemes + 1));
		var longCluster = MarkupText.Plain("e" + new string('\u0301', StringDistance.MaxInputCodeUnits));
		foreach (var input in new[] { many, longCluster })
		{
			await Assert.That(StringDistance.TryCalculate(input, MarkupText.Empty, out _)).IsFalse();
			await Assert.That(StringDistance.TryCalculate(input, input, out _)).IsFalse();
		}
		await Assert.That(StringDistance.TryCalculate(MarkupText.Empty,
			MarkupText.Plain(new string('x', StringDistance.MaxInputGraphemes)), out var distance)).IsTrue();
		await Assert.That(distance).IsEqualTo(StringDistance.MaxInputGraphemes);
	}
}
