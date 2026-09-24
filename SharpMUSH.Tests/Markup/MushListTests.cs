using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ParserInterfaces;
using M = MarkupString.Ansi.AnsiMarkup;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// <c>words</c>, <c>first</c>, <c>rest</c> and <c>extract</c> share one interruptible scan that allocates
/// nothing for an item it does not return (#977). The reference here is what they used to be: split the
/// whole list with <see cref="MushText.SplitList"/>, then <c>Skip</c>/<c>Take</c>/<c>Join</c>.
/// </summary>
public class MushListTests
{
	private static MarkupText Red(string text)
		=> MarkupText.Wrap(M.Create(foreground: new AnsiColor.Standard(1, false)), MarkupText.Plain(text));

	private static MarkupText Plain(string text) => MarkupText.Plain(text);

	private static readonly MarkupText[] Texts =
	[
		Plain(""), Plain(" "), Plain("a"), Plain("a b c"), Plain("  a  b   c  "), Plain("a|b|c"), Plain("|a||b|"),
		Plain("||"), Plain("a,b,c,"), Plain("one two  three|four|five six"),
		// Delimiters against grapheme clusters: a combining mark, a surrogate pair and a ZWJ sequence.
		Plain("á b́"), Plain(" ́ x"), Plain("x ́"), Plain("a ́ b"), Plain("😀 a😀 b"),
		Plain("é|é"), Plain("áb́"), Plain("👨‍👩 x 👨‍👩"),
		Plain("ab ab  ab"), Plain("abab"),
		// Markup on the whole text, and on part of it, including a delimiter.
		Red("a b c"), Red("a|b|c"), Red("  a  b  "),
		MarkupText.Concat([Red("a"), Plain(" b "), Red("c")]),
		MarkupText.Concat([Plain("a"), Red("|"), Plain("b"), Red("|"), Plain("c")]),
		MarkupText.Concat([Plain("x "), Red(" "), Plain(" y")]),
		MarkupText.Concat([Plain("a  "), Red("b c"), Plain("  d")]),
		MarkupText.Concat([Red("a|"), Plain("b|c"), Red("|d")]),
	];

	private static readonly MarkupText[] Delimiters =
	[
		Plain(" "), Plain("|"), Plain(","), Plain("ab"), Plain("́"), Plain("😀"), Plain("‍"), Plain("a"), Plain(""),
		Red("|"), Red(" "),
	];

	private static readonly int[] Positions = [int.MinValue, -10, -3, -2, -1, 0, 1, 2, 3, 10, int.MaxValue];

	private static MarkupText[] Split(MarkupText delimiter, MarkupText text) => MushText.SplitList(delimiter, text);

	private static MarkupText OldFirst(MarkupText d, MarkupText t) => Split(d, t).FirstOrDefault() ?? MarkupText.Empty;

	private static MarkupText OldRest(MarkupText d, MarkupText t) => MarkupText.Join(d, Split(d, t).Skip(1));

	private static MarkupText OldExtract(MarkupText d, MarkupText t, int first, int length)
	{
		var list = Split(d, t);
		var range = first > 0
			? list.Skip(first - 1)
			: Enumerable.TakeLast(list, (int)Math.Min(int.MaxValue, Math.Abs((long)first)));
		var result = length > 0
			? range.Take(length)
			: Enumerable.TakeLast(range, (int)Math.Min(int.MaxValue, Math.Abs((long)length)));
		return MarkupText.Join(d, result);
	}

	private static string Describe(MarkupText text) => $"'{text.Text}'/{text.Render(MarkupFormat.Html)}";

	/// <summary><see cref="MarkupText.Equals(MarkupText?)"/> compares the text alone, so the markup is compared as rendered.</summary>
	private static bool Same(MarkupText expected, MarkupText actual)
		=> expected.Equals(actual) && expected.Render(MarkupFormat.Html) == actual.Render(MarkupFormat.Html);

	[Test]
	public async Task TheSharedScanAgreesWithSplittingTheWholeList()
	{
		var differences = new List<string>();
		void Check(string what, MarkupText delimiter, MarkupText text, MarkupText expected, MarkupText actual)
		{
			if (!Same(expected, actual))
				differences.Add($"{what}({Describe(text)}, {Describe(delimiter)}): expected {Describe(expected)}, got {Describe(actual)}");
		}

		foreach (var text in Texts)
			foreach (var delimiter in Delimiters)
			{
				var expectedCount = Split(delimiter, text).Length;
				if (MushList.Count(delimiter, text) != expectedCount)
					differences.Add($"Count({Describe(text)}, {Describe(delimiter)}): expected {expectedCount}");
				Check("First", delimiter, text, OldFirst(delimiter, text), MushList.First(delimiter, text));
				Check("Rest", delimiter, text, OldRest(delimiter, text), MushList.Rest(delimiter, text));
				foreach (var first in Positions)
					foreach (var length in Positions)
						Check($"Extract({first},{length})", delimiter, text,
							OldExtract(delimiter, text, first, length), MushList.Extract(delimiter, text, first, length));
			}

		await Assert.That(differences.Take(10)).IsEmpty();
	}

	[Test]
	public async Task RandomListsAgreeWithSplittingTheWholeList()
	{
		string[] alphabet = ["a", "b", " ", "|", "́", "😀", "‍", "ab", ","];
		string[] delimiters = ["|", " ", "a", "́", "ab", ",", "😀"];
		var random = new Random(1184);
		var differences = new List<string>();
		for (var i = 0; i < 2000; i++)
		{
			var text = MarkupText.Concat(Enumerable.Range(0, random.Next(0, 14))
				.Select(_ => alphabet[random.Next(alphabet.Length)])
				.Select(piece => random.Next(4) == 0 ? Red(piece) : Plain(piece)));
			var delimiter = delimiters[random.Next(delimiters.Length)] is var raw && random.Next(6) == 0 ? Red(raw) : Plain(raw);
			var first = random.Next(-4, 6);
			var length = random.Next(-4, 6);

			if (MushList.Count(delimiter, text) != Split(delimiter, text).Length
				|| !Same(OldFirst(delimiter, text), MushList.First(delimiter, text))
				|| !Same(OldRest(delimiter, text), MushList.Rest(delimiter, text))
				|| !Same(OldExtract(delimiter, text, first, length), MushList.Extract(delimiter, text, first, length)))
				differences.Add($"{Describe(text)} on {Describe(delimiter)} extract({first},{length})");
		}

		await Assert.That(differences.Take(10)).IsEmpty();
	}

	/// <summary>An input at the output ceiling made of nothing but delimiters and one-character items.</summary>
	private static MarkupText NearCeiling(string unit)
		=> Plain(string.Concat(Enumerable.Repeat(unit, FunctionLimits.MaxOutputCodeUnits / unit.Length)));

	private static long Allocated(Action action)
	{
		var before = GC.GetAllocatedBytesForCurrentThread();
		action();
		return GC.GetAllocatedBytesForCurrentThread() - before;
	}

	/// <summary>
	/// The old path made one object per item — 2.6 million of them here, hundreds of megabytes. What is
	/// asked for decides the cost now: a count, a first item, a narrow range and a tail-count allocate a few
	/// kilobytes, however long the text.
	/// </summary>
	[Test]
	[Arguments(" ")]
	[Arguments("|")]
	public async Task ScanningANearCeilingListAllocatesNothingForTheItemsItSkips(string delimiter)
	{
		var text = NearCeiling("a" + delimiter);
		var separator = Plain(delimiter);
		const long Small = 64 * 1024;

		await Assert.That(Allocated(() => Assert.That(MushList.Count(separator, text)).IsGreaterThan(1_000_000).GetAwaiter().GetResult()))
			.IsLessThan(Small);
		await Assert.That(Allocated(() => MushList.First(separator, text))).IsLessThan(Small);
		await Assert.That(Allocated(() => MushList.Extract(separator, text, 3, 2))).IsLessThan(Small)
			.Because("a narrow range near the front");
		await Assert.That(Allocated(() => MushList.Extract(separator, text, 1_000_000, 2))).IsLessThan(Small)
			.Because("a narrow range in the middle of a long text");
		await Assert.That(Allocated(() => MushList.Extract(separator, text, -2, 1))).IsLessThan(Small)
			.Because("a narrow range at the end, after counting a long list");
	}

	/// <summary>The tail is the answer, so it may cost its own size — once — and no more.</summary>
	[Test]
	public async Task RestCostsAboutTheSizeOfItsAnswer()
	{
		var text = NearCeiling("a|");

		var allocated = Allocated(() => MushList.Rest(Plain("|"), text));

		await Assert.That(allocated).IsLessThan(4L * text.Length * sizeof(char));
	}

	/// <summary>
	/// Markup on the list or on the delimiter changes what is spliced into the answer, not what it costs:
	/// the gaps with markup are replaced, and the rest of the run is still one slice.
	/// </summary>
	[Test]
	public async Task RestOfAMarkedListCostsAboutTheSizeOfItsAnswer()
	{
		var plain = NearCeiling("a|");
		var marked = MarkupText.Concat([Red("a|"), plain.Substring(2), Red("|a")]);

		var allocated = Allocated(() => MushList.Rest(Plain("|"), marked));

		await Assert.That(allocated).IsLessThan(6L * plain.Length * sizeof(char));
	}

	/// <summary>A long range is put together a piece at a time and joined, which is the same as joining every item.</summary>
	[Test]
	public async Task ALongRangeWithMarkupAgreesWithSplittingTheWholeList()
	{
		var items = Enumerable.Range(0, 10_000).Select(i => i % 3 == 0 ? Red($"{i}") : Plain($"{i}")).ToArray();
		var markedDelimiters = MarkupText.Join(Red("|"), items);
		var markedCuts = MarkupText.Join(Plain("|́"), items);

		foreach (var (text, delimiter) in new[]
		{
			(markedDelimiters, Plain("|")), (markedDelimiters, Red("|")), (MarkupText.Join(Plain("|"), items), Red("|")),
			(markedCuts, Plain("|")),
		})
		{
			await Assert.That(Same(OldRest(delimiter, text), MushList.Rest(delimiter, text))).IsTrue();
			await Assert.That(Same(OldExtract(delimiter, text, 7, 9_000), MushList.Extract(delimiter, text, 7, 9_000))).IsTrue();
		}
	}

	/// <summary>One item as long as the whole text is searched under the budget too, not in one go.</summary>
	[Test]
	public async Task ALongItemIsScannedUnderTheExecutionBudget()
	{
		var text = NearCeiling("a");
		using var cancelled = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancelled.Token);
		using var scope = budget.Enter();
		cancelled.Cancel();

		await Assert.That(() => MushList.First(Plain("|"), text)).Throws<OperationCanceledException>();
		await Assert.That(() => MushList.Count(Plain("|"), text)).Throws<OperationCanceledException>();
		await Assert.That(() => MushList.Extract(Plain("|"), text, 1, 1)).Throws<OperationCanceledException>();
	}

	/// <summary>
	/// The scan is under the ambient execution budget: one that has run out ends it at the next checkpoint
	/// rather than after the whole text.
	/// </summary>
	[Test]
	public async Task ScanningStopsWhenTheExecutionBudgetIsGone()
	{
		var text = NearCeiling("a|");
		using var cancelled = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancelled.Token);
		using var scope = budget.Enter();
		cancelled.Cancel();

		await Assert.That(() => MushList.Count(Plain("|"), text)).Throws<OperationCanceledException>();
		await Assert.That(() => MushList.First(Plain("|"), text)).ThrowsNothing()
			.Because("first() stops at the first item and never reaches a checkpoint");
		await Assert.That(() => MushList.Extract(Plain("|"), text, 1_000_000, 1)).Throws<OperationCanceledException>();
		await Assert.That(() => MushList.Rest(Plain("|"), text)).Throws<OperationCanceledException>();
	}
}
