public class MarkupTextOperationsTests
{
	private sealed record Tag(string Name) : IMarkup;

	private static readonly Tag Red = new("red");
	private static readonly Tag Blue = new("blue");
	private static readonly Tag Green = new("green");

	private static async Task AssertRunsSortedByStart(MarkupText text)
	{
		for (var i = 1; i < text.Runs.Length; i++)
			await Assert.That(text.Runs[i].Start).IsGreaterThanOrEqualTo(text.Runs[i - 1].Start);
	}

	// Substring

	[Test]
	public async Task Substring_PlainText_ExtractsCorrectRange()
	{
		var sub = MarkupText.Plain("Hello, World!").Substring(7, 5);

		await Assert.That(sub.ToPlainText()).IsEqualTo("World");
		await Assert.That(sub.Length).IsEqualTo(5);
	}

	[Test]
	public async Task Substring_AcrossRuns_ClipsRuns()
	{
		var combined = MarkupText.Concat(MarkupText.Wrap(Red, "Hello"), MarkupText.Wrap(Blue, " World"));

		var sub = combined.Substring(2, 5);

		await Assert.That(sub.ToPlainText()).IsEqualTo("llo W");
		await Assert.That(sub.Length).IsEqualTo(5);
		await Assert.That(sub.Runs.Length).IsEqualTo(2);
	}

	[Test]
	public async Task Substring_BeyondLength_ReturnsEmpty()
		=> await Assert.That(MarkupText.Plain("Hi").Substring(10, 5).Length).IsEqualTo(0);

	[Test]
	public async Task Substring_ZeroLength_ReturnsEmpty()
		=> await Assert.That(MarkupText.Plain("Hello").Substring(0, 0).Length).IsEqualTo(0);

	[Test]
	public async Task Substring_ToEnd_TakesRemainder()
		=> await Assert.That(MarkupText.Plain("Hello").Substring(2).Text).IsEqualTo("llo");

	[Test]
	public async Task Substring_NeverSplitsSurrogatePair()
	{
		var t = MarkupText.Plain("a\U0001F600b");

		// The end snaps down to a boundary.
		await Assert.That(t.Substring(0, 2).Text).IsEqualTo("a");

		// (2, 2) is "the rest of the string" spelled out as start + (Length - start): the caller's
		// own range already reaches the end, so the tail survives even though the start snaps back
		// into the emoji cluster.
		await Assert.That(t.Substring(2, 2).Text).IsEqualTo("\U0001F600b");
	}

	[Test]
	public async Task Substring_LengthIsCountedFromTheSnappedStart()
	{
		// The window [1,3) lands inside the "\u00e9" cluster and does not reach the end of this
		// four-code-unit text; snapping the start back to 0 must not push the end out past what was
		// asked for, which would return three code units for a request of two.
		await Assert.That(MarkupText.Plain("e\u0301xy").Substring(1, 2).Text).IsEqualTo("e\u0301");
	}

	[Test]
	public async Task Substring_ToEnd_TakesTheWholeRemainderFromTheSnappedStart()
	{
		// The one-argument overload has no length to shorten: an index inside a cluster snaps back
		// and everything from there survives.
		await Assert.That(MarkupText.Plain("a\U0001F600b").Substring(2).Text).IsEqualTo("\U0001F600b");
	}

	[Test]
	public async Task Substring_RangeReachingTheEnd_KeepsTheTailEvenWhenStartIsInsideACluster()
	{
		// The "rest of the string" idiom (x.Substring(n, x.Length - n)) has to decide the shortcut
		// from the caller's own arguments, before start snaps back: deciding it from the snapped
		// start instead would let the inward snap swallow code units off the tail.

		// start=1 lands on the low surrogate of the leading emoji; the range still reaches the end.
		await Assert.That(MarkupText.Plain("\U0001F600abc").Substring(1, 4).Text).IsEqualTo("\U0001F600abc");

		// Same shape with the cluster in the middle: start=2 lands on the low surrogate.
		await Assert.That(MarkupText.Plain("a\U0001F600bc").Substring(2, 3).Text).IsEqualTo("\U0001F600bc");

		// Decomposed "cafe\u0301 x" (\u0301 = combining acute on the preceding e): start=4
		// lands on the combining mark itself.
		await Assert.That(MarkupText.Plain("cafe\u0301 x").Substring(4, 3).Text).IsEqualTo("e\u0301 x");
	}

	[Test]
	public async Task Substring_InteriorRangeInsideACluster_NeverExceedsTheRequestedLength()
	{
		// [1,2) lands entirely inside the decomposed "e\u0301" cluster and does not reach the end of
		// this three-code-unit text: from snaps back to 0, and to - measured as snapped-start + length -
		// snaps back to 0 as well, since it must not exceed the one code unit requested. The result
		// is empty rather than the whole cluster, which would be two code units for a request of one.
		await Assert.That(MarkupText.Plain("e\u0301x").Substring(1, 1).Text).IsEqualTo("");
	}

	[Test]
	public async Task Substring_KeepsCombiningMarkWithBase()
	{
		var t = MarkupText.Plain("e\u0301x");
		await Assert.That(t.Substring(0, 1).Text).IsEqualTo("");                  // cannot end inside cluster: snaps to 0
		await Assert.That(t.Substring(0, 2).Text).IsEqualTo("e\u0301");
	}

	[Test]
	public async Task Substring_KeepsHangulSyllableWhole()
	{
		// Conjoining jamo form one cluster: L+V, L+V+T, and a precomposed LV followed by a T.
		await Assert.That(MarkupText.Plain("\u1100\u1161").Substring(0, 1).Text).IsEqualTo("");
		await Assert.That(MarkupText.Plain("\u1100\u1161\u11A8").Substring(0, 1).Text).IsEqualTo("");
		await Assert.That(MarkupText.Plain("\u1100\u1161\u11A8").Substring(0, 2).Text).IsEqualTo("");
		await Assert.That(MarkupText.Plain("\uAC00\u11A8").Substring(0, 1).Text).IsEqualTo("");
		await Assert.That(MarkupText.Plain("\u1100\u1161x").Substring(0, 2).Text).IsEqualTo("\u1100\u1161");
	}

	[Test]
	public async Task Substring_KeepsPrependCharacterWithWhatItPrepends()
	{
		await Assert.That(MarkupText.Plain("\u0600\u0661").Substring(0, 1).Text).IsEqualTo("");
		await Assert.That(MarkupText.Plain("\u0600\u0661").Substring(0, 2).Text).IsEqualTo("\u0600\u0661");
	}

	// Split

	[Test]
	public async Task Split_PlainText_SplitsCorrectly()
	{
		var parts = MarkupText.Plain("a,b,c").Split(",");

		await Assert.That(parts.Length).IsEqualTo(3);
		await Assert.That(parts[0].ToPlainText()).IsEqualTo("a");
		await Assert.That(parts[1].ToPlainText()).IsEqualTo("b");
		await Assert.That(parts[2].ToPlainText()).IsEqualTo("c");
	}

	[Test]
	public async Task Split_NoDelimiter_ReturnsSingle()
	{
		var parts = MarkupText.Plain("hello").Split(",");

		await Assert.That(parts.Length).IsEqualTo(1);
		await Assert.That(parts[0].ToPlainText()).IsEqualTo("hello");
	}

	/// <summary>
	/// An empty delimiter matches nothing rather than every position: the text comes back whole, as
	/// the one segment. Splitting into characters is <c>text.Select(…)</c>'s job, not this overload's.
	/// </summary>
	[Test]
	public async Task Split_EmptyDelimiter_ReturnsSingle()
	{
		var text = MarkupText.Plain("hello");

		var parts = text.Split("");

		await Assert.That(parts.Length).IsEqualTo(1);
		await Assert.That(parts[0]).IsSameReferenceAs(text);
	}

	[Test]
	public async Task Split_EmptyDelimiterOnEmptyText_ReturnsNoSegments()
		=> await Assert.That(MarkupText.Empty.Split("").Length).IsEqualTo(0);

	[Test]
	public async Task Split_MarkupTextDelimiter_MatchesStringDelimiter()
	{
		var parts = MarkupText.Plain("a,b").Split(MarkupText.Wrap(Red, ","));

		await Assert.That(parts.Length).IsEqualTo(2);
		await Assert.That(parts[1].ToPlainText()).IsEqualTo("b");
	}

	// Trim

	[Test]
	public async Task Trim_BothSides_TrimsCorrectly()
		=> await Assert.That(MarkupText.Plain("  hello  ").Trim(TrimType.TrimBoth).ToPlainText()).IsEqualTo("hello");

	[Test]
	public async Task Trim_StartOnly_TrimsCorrectly()
		=> await Assert.That(MarkupText.Plain("  hello  ").Trim(TrimType.TrimStart).ToPlainText()).IsEqualTo("hello  ");

	[Test]
	public async Task Trim_EndOnly_TrimsCorrectly()
		=> await Assert.That(MarkupText.Plain("  hello  ").Trim(TrimType.TrimEnd).ToPlainText()).IsEqualTo("  hello");

	[Test]
	public async Task Trim_MarkupTextChars_TrimsCorrectly()
		=> await Assert.That(MarkupText.Plain("xxhixx").Trim(TrimType.TrimBoth, MarkupText.Plain("x")).ToPlainText())
			.IsEqualTo("hi");

	// Pad and Center

	[Test]
	public async Task Pad_Right_PadsCorrectly()
	{
		var result = MarkupText.Plain("Hi").Pad(MarkupText.Space, 5, PadType.Right, TruncationType.Overflow);

		await Assert.That(result.ToPlainText()).IsEqualTo("Hi   ");
		await Assert.That(result.Length).IsEqualTo(5);
	}

	[Test]
	public async Task Pad_Left_PadsCorrectly()
		=> await Assert.That(MarkupText.Plain("Hi").Pad(MarkupText.Space, 5, PadType.Left, TruncationType.Overflow)
			.ToPlainText()).IsEqualTo("   Hi");

	[Test]
	public async Task Pad_Center_PadsCorrectly()
		=> await Assert.That(MarkupText.Plain("Hi").Pad(MarkupText.Plain("-"), 6, PadType.Center, TruncationType.Overflow)
			.ToPlainText()).IsEqualTo("--Hi--");

	[Test]
	public async Task Pad_Full_DistributesSpacesBetweenWords()
		=> await Assert.That(MarkupText.Plain("a b c").Pad(MarkupText.Space, 9, PadType.Full, TruncationType.Overflow)
			.ToPlainText()).IsEqualTo("a   b   c");

	[Test]
	public async Task Pad_Overflow_LeavesLongerTextUntouched()
		=> await Assert.That(MarkupText.Plain("abcdef").Pad(MarkupText.Space, 3, PadType.Right, TruncationType.Overflow)
			.ToPlainText()).IsEqualTo("abcdef");

	[Test]
	public async Task Pad_Truncate_CutsLongerTextToWidth()
		=> await Assert.That(MarkupText.Plain("abcdef").Pad(MarkupText.Space, 3, PadType.Right, TruncationType.Truncate)
			.ToPlainText()).IsEqualTo("abc");

	[Test]
	public async Task Pad_UsesDisplayWidth()
	{
		var t = MarkupText.Plain("\u65E5\u672C").Pad(MarkupText.Plain("."), 6, PadType.Right, TruncationType.Truncate);
		await Assert.That(t.Text).IsEqualTo("\u65E5\u672C..");
	}

	[Test]
	public async Task Pad_TruncatesByDisplayWidthWithoutSplittingWideChar()
	{
		var t = MarkupText.Plain("\u65E5\u672C\u8A9E").Pad(MarkupText.Space, 5, PadType.Right, TruncationType.Truncate);
		await Assert.That(t.Text).IsEqualTo("\u65E5\u672C ");
	}

	[Test]
	public async Task Pad_PreservesFillMarkup()
	{
		var result = MarkupText.Plain("Hi").Pad(MarkupText.Wrap(Red, "."), 4, PadType.Right, TruncationType.Overflow);

		await Assert.That(result.Text).IsEqualTo("Hi..");
		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(result.Runs[0]).IsEqualTo(new Run(2, 2, MarkupSet.Of(Red)));
	}

	[Test]
	public async Task Pad_FillWiderThanTheCellsLeftToFill_StillReachesWidth()
	{
		var result = MarkupText.Plain("Hi").Pad(MarkupText.Plain("\u65E5"), 5, PadType.Right, TruncationType.Overflow);

		await Assert.That(result.Text).IsEqualTo("Hi\u65E5 ");
		await Assert.That(result.DisplayWidth).IsEqualTo(5);
	}

	[Test]
	public async Task Pad_TruncateDeficitTooSmallForTheFill_TakesSpaces()
	{
		var result = MarkupText.Plain("\u65E5\u672C\u8A9E").Pad(MarkupText.Plain("\u65E5"), 5, PadType.Right, TruncationType.Truncate);

		await Assert.That(result.Text).IsEqualTo("\u65E5\u672C ");
		await Assert.That(result.DisplayWidth).IsEqualTo(5);
	}

	[Test]
	public async Task Pad_Full_TruncateFillsTheDeficitLikeTheOtherPadTypes()
	{
		var result = MarkupText.Plain("\u65E5\u672C\u8A9E").Pad(MarkupText.Space, 5, PadType.Full, TruncationType.Truncate);

		await Assert.That(result.Text).IsEqualTo("\u65E5\u672C ");
		await Assert.That(result.DisplayWidth).IsEqualTo(5);
	}

	[Test]
	public async Task Center_FillWiderThanTheCellsLeftToFill_StillReachesWidth()
	{
		var result = MarkupText.Plain("Hi").Center(MarkupText.Plain("\u65E5"), MarkupText.Plain("\u65E5"), 7, TruncationType.Overflow);

		await Assert.That(result.DisplayWidth).IsEqualTo(7);
		await Assert.That(result.Text).IsEqualTo("\u65E5Hi\u65E5 ");
	}

	[Test]
	public async Task Center_UsesBothFills()
	{
		var result = MarkupText.Plain("Hi")
			.Center(MarkupText.Plain("<"), MarkupText.Plain(">"), 6, TruncationType.Overflow);

		await Assert.That(result.ToPlainText()).IsEqualTo("<<Hi>>");
	}

	[Test]
	public async Task Center_TruncatesByDisplayWidth()
	{
		var result = MarkupText.Plain("\u65E5\u672C\u8A9E")
			.Center(MarkupText.Space, MarkupText.Space, 5, TruncationType.Truncate);

		await Assert.That(result.Text).IsEqualTo("\u65E5\u672C ");
	}

	// Repeat

	[Test]
	public async Task Repeat_ThreeTimes_RepeatsCorrectly()
	{
		var result = MarkupText.Plain("ab").Repeat(3);

		await Assert.That(result.ToPlainText()).IsEqualTo("ababab");
		await Assert.That(result.Length).IsEqualTo(6);
	}

	[Test]
	public async Task Repeat_ZeroTimes_ReturnsEmpty()
		=> await Assert.That(MarkupText.Plain("ab").Repeat(0).Length).IsEqualTo(0);

	// Remove, Replace, Insert

	[Test]
	public async Task Remove_MiddleSection_RemovesCorrectly()
		=> await Assert.That(MarkupText.Plain("Hello World").Remove(5, 1).ToPlainText()).IsEqualTo("HelloWorld");

	[Test]
	public async Task Replace_MiddleSection_ReplacesCorrectly()
		=> await Assert.That(MarkupText.Plain("Hello World").Replace(6, 0, MarkupText.Plain("Beautiful "))
			.ToPlainText()).IsEqualTo("Hello Beautiful World");

	[Test]
	public async Task InsertAt_Middle_InsertsCorrectly()
		=> await Assert.That(MarkupText.Plain("HelloWorld").Insert(5, MarkupText.Space).ToPlainText())
			.IsEqualTo("Hello World");

	[Test]
	public async Task Insert_InheritsEnclosingRunMarkup()
	{
		var t = MarkupText.Wrap(new Tag("b"), "abcd").Insert(2, MarkupText.Plain("X"));
		await Assert.That(t.Runs.Length).IsEqualTo(1);
		await Assert.That(t.Text).IsEqualTo("abXcd");
	}

	[Test]
	public async Task Insert_AtRunBoundary_StaysPlain()
	{
		var t = MarkupText.Wrap(Red, "abcd").Insert(4, MarkupText.Plain("X"));

		await Assert.That(t.Text).IsEqualTo("abcdX");
		await Assert.That(t.Runs.Length).IsEqualTo(1);
		await Assert.That(t.Runs[0]).IsEqualTo(new Run(0, 4, MarkupSet.Of(Red)));
	}

	// Splice and ReplaceAll

	[Test]
	public async Task Splice_AppliesEditsInOnePass()
	{
		var t = MarkupText.Plain("a  b   c");
		var r = t.ReplaceAll("  ", MarkupText.Space);
		await Assert.That(r.Text).IsEqualTo("a b  c");   // non-overlapping left-to-right matches
	}

	[Test]
	public async Task ReplaceAll_PreservesMarkupOutsideMatches()
	{
		var t = MarkupText.Concat([
			MarkupText.Wrap(new Tag("b"), "ab"), MarkupText.Plain("  "), MarkupText.Wrap(new Tag("b"), "cd")
		]);
		var r = t.ReplaceAll("  ", MarkupText.Space);
		await Assert.That(r.Text).IsEqualTo("ab cd");
		await Assert.That(r.Runs.Length).IsEqualTo(2);
	}

	[Test]
	public async Task Splice_EditInsideACluster_RemovesTheWholeCluster()
	{
		// "a" + a ZWJ family emoji + "b": an edit landing inside the emoji snaps out over it.
		var t = MarkupText.Plain("a\U0001F468\u200D\U0001F469\u200D\U0001F467b");
		var r = t.Splice([new Edit(2, 2, MarkupText.Plain("-"))]);

		await Assert.That(r.Text).IsEqualTo("a-b");
	}

	[Test]
	public async Task Splice_UnsortedEdits_Throws()
	{
		var t = MarkupText.Plain("abcdef");
		Edit[] edits = [new(3, 1, MarkupText.Empty), new(1, 1, MarkupText.Empty)];

		await Assert.That(() => t.Splice(edits)).Throws<ArgumentException>();
	}

	[Test]
	public async Task Splice_OverlappingEdits_Throws()
	{
		var t = MarkupText.Plain("abcdef");
		Edit[] edits = [new(1, 3, MarkupText.Empty), new(2, 1, MarkupText.Empty)];

		await Assert.That(() => t.Splice(edits)).Throws<ArgumentException>();
	}

	[Test]
	public async Task Splice_OutOfRangeEdit_Throws()
	{
		var t = MarkupText.Plain("abc");
		Edit[] edits = [new(2, 5, MarkupText.Empty)];

		await Assert.That(() => t.Splice(edits)).Throws<ArgumentOutOfRangeException>();
	}

	// Searching

	[Test]
	public async Task IndexOf_Found_ReturnsCorrectIndex()
		=> await Assert.That(MarkupText.Plain("Hello, World!").IndexOf("World")).IsEqualTo(7);

	[Test]
	public async Task IndexOf_NotFound_ReturnsNegativeOne()
		=> await Assert.That(MarkupText.Plain("Hello").IndexOf("xyz")).IsEqualTo(-1);

	[Test]
	public async Task LastIndexOf_ReturnsLastMatch()
		=> await Assert.That(MarkupText.Plain("abab").LastIndexOf("ab")).IsEqualTo(2);

	[Test]
	public async Task IndexesOf_ReturnsEveryNonOverlappingMatch()
		=> await Assert.That(MarkupText.Plain("aXaXa").IndexesOf("a").ToArray()).IsEquivalentTo(new[] { 0, 2, 4 });

	// Apply, Map, AttachTail

	[Test]
	public async Task Apply_ToUpper_TransformsText()
		=> await Assert.That(MarkupText.Plain("hello").Apply(s => s.ToUpperInvariant()).ToPlainText()).IsEqualTo("HELLO");

	[Test]
	public async Task Apply_SameLength_KeepsRuns()
	{
		var result = MarkupText.Wrap(Red, "hello").Apply(s => s.ToUpperInvariant());

		await Assert.That(result.Text).IsEqualTo("HELLO");
		await Assert.That(result.Runs.Length).IsEqualTo(1);
	}

	[Test]
	public async Task Apply_DifferentLength_DropsRuns()
	{
		var result = MarkupText.Wrap(Red, "hello").Apply(s => s + "!");

		await Assert.That(result.Text).IsEqualTo("hello!");
		await Assert.That(result.Runs.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Map_TransformsEveryRunAndGap()
	{
		var t = MarkupText.Concat(MarkupText.Plain("ab"), MarkupText.Wrap(Red, "cd"));

		var result = t.Map(segment => MarkupText.Plain(segment.Text.ToUpperInvariant()));

		await Assert.That(result.Text).IsEqualTo("ABCD");
	}

	[Test]
	public async Task AttachTail_InheritsOutermostMarkupOfTrailingRun()
	{
		var result = MarkupText.Wrap(Red, "ab").AttachTail(MarkupText.Plain("cd"));

		await Assert.That(result.Text).IsEqualTo("abcd");
		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(result.Runs[0]).IsEqualTo(new Run(0, 4, MarkupSet.Of(Red)));
	}

	[Test]
	public async Task AttachTail_PlainTail_WhenLastRunDoesNotReachEnd()
	{
		var t = MarkupText.Concat(MarkupText.Wrap(Red, "ab"), MarkupText.Plain("cd"));

		var result = t.AttachTail(MarkupText.Plain("ef"));

		await Assert.That(result.Text).IsEqualTo("abcdef");
		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(result.Runs[0].Length).IsEqualTo(2);
	}

	// DisplayWidth

	[Test]
	public async Task DisplayWidth_MeasuresCells()
	{
		await Assert.That(MarkupText.Plain("abc").DisplayWidth).IsEqualTo(3);
		await Assert.That(MarkupText.Wrap(Red, "\u65E5\u672C").DisplayWidth).IsEqualTo(4);
	}

	// Immutability

	[Test]
	public async Task Immutability_SubstringDoesNotMutateOriginal()
	{
		var t = MarkupText.Plain("Hello, World!");
		var sub = t.Substring(7, 5);

		await Assert.That(t.ToPlainText()).IsEqualTo("Hello, World!");
		await Assert.That(t.Length).IsEqualTo(13);
		await Assert.That(sub.ToPlainText()).IsEqualTo("World");
	}

	[Test]
	public async Task Immutability_RemoveDoesNotMutateOriginal()
	{
		var t = MarkupText.Plain("Hello World");
		var removed = t.Remove(5, 1);

		await Assert.That(t.ToPlainText()).IsEqualTo("Hello World");
		await Assert.That(removed.ToPlainText()).IsEqualTo("HelloWorld");
	}

	// Run ordering invariants

	[Test]
	public async Task SortOrder_Substring_RunsRemainSorted()
	{
		var combined = MarkupText.Concat(MarkupText.Wrap(Red, "Hello"), MarkupText.Wrap(Blue, " World"));
		var sub = combined.Substring(3, 5);

		await AssertRunsSortedByStart(sub);
		await Assert.That(sub.Runs[0].Start).IsEqualTo(0);
	}

	[Test]
	public async Task SortOrder_Remove_RunsRemainSorted()
	{
		var abc = MarkupText.Concat([MarkupText.Wrap(Red, "AA"), MarkupText.Wrap(Blue, "BB"), MarkupText.Wrap(Green, "CC")]);
		var result = abc.Remove(2, 2);

		await Assert.That(result.Text).IsEqualTo("AACC");
		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_Replace_RunsRemainSorted()
	{
		var result = MarkupText.Wrap(Red, "Hello World").Replace(5, 1, MarkupText.Wrap(Blue, "XX"));

		await Assert.That(result.Text).IsEqualTo("HelloXXWorld");
		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_InsertAt_RunsRemainSorted()
	{
		var result = MarkupText.Wrap(Red, "HelloWorld").Insert(5, MarkupText.Wrap(Blue, " "));

		await Assert.That(result.Text).IsEqualTo("Hello World");
		await AssertRunsSortedByStart(result);
	}

	[Test]
	public async Task SortOrder_Split_AllSegmentsRemainSorted()
	{
		var combined = MarkupText.Concat(MarkupText.Wrap(Red, "Hello"), MarkupText.Wrap(Blue, " World"));
		var parts = combined.Split(" ");

		foreach (var part in parts)
		{
			await AssertRunsSortedByStart(part);
			if (part.Runs.Length > 0) await Assert.That(part.Runs[0].Start).IsEqualTo(0);
		}
	}

	[Test]
	public async Task SortOrder_Repeat_RunsRemainSorted()
	{
		var result = MarkupText.Wrap(Red, "AB").Repeat(5);

		// Five like-marked copies coalesce into one run at construction.
		await Assert.That(result.Runs.Length).IsEqualTo(1);
		await Assert.That(result.ToPlainText()).IsEqualTo("ABABABABAB");
		await AssertRunsSortedByStart(result);
	}
}
