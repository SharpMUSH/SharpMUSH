public class MarkupTextTests
{
	private sealed record Tag(string Name) : IMarkup;

	[Test]
	public async Task Plain_HasNoRuns()
	{
		var t = MarkupText.Plain("abc");
		await Assert.That(t.Runs.Length).IsEqualTo(0);
		await Assert.That(t.Text).IsEqualTo("abc");
		await Assert.That(t.Length).IsEqualTo(3);
	}

	[Test]
	public async Task Wrap_CreatesOneRunCoveringText()
	{
		var t = MarkupText.Wrap(new Tag("b"), "abc");
		await Assert.That(t.Runs.Length).IsEqualTo(1);
		await Assert.That(t.Runs[0]).IsEqualTo(new Run(0, 3, MarkupSet.Of(new Tag("b"))));
	}

	[Test]
	public async Task Wrap_EmptyText_IsEmpty()
		=> await Assert.That(MarkupText.Wrap(new Tag("b"), "")).IsSameReferenceAs(MarkupText.Empty);

	[Test]
	public async Task Concat_PlainThenStyled_KeepsGapAsPlain()
	{
		var t = MarkupText.Concat(MarkupText.Plain("ab"), MarkupText.Wrap(new Tag("b"), "c"));
		await Assert.That(t.Text).IsEqualTo("abc");
		await Assert.That(t.Runs.Length).IsEqualTo(1);
		await Assert.That(t.Runs[0].Start).IsEqualTo(2);
	}

	[Test]
	public async Task Concat_AdjacentEqualSets_Coalesce()
	{
		var t = MarkupText.Concat(MarkupText.Wrap(new Tag("b"), "a"), MarkupText.Wrap(new Tag("b"), "b"));
		await Assert.That(t.Runs.Length).IsEqualTo(1);
		await Assert.That(t.Runs[0].Length).IsEqualTo(2);
	}

	[Test]
	public async Task Wrap_Inner_AddsOuterLayerToEveryRunAndGap()
	{
		var inner = MarkupText.Concat(MarkupText.Plain("a"), MarkupText.Wrap(new Tag("i"), "b"));
		var t = MarkupText.Wrap(new Tag("o"), inner);
		await Assert.That(t.Runs.Length).IsEqualTo(2);
		await Assert.That(t.Runs[0].Markups).IsEqualTo(MarkupSet.Of(new Tag("o")));
		await Assert.That(t.Runs[1].Markups).IsEqualTo(MarkupSet.Of([new Tag("i"), new Tag("o")]));
	}

	[Test]
	public async Task Join_InsertsSeparatorBetweenParts()
	{
		var t = MarkupText.Join(MarkupText.Plain(", "), [MarkupText.Plain("a"), MarkupText.Plain("b")]);
		await Assert.That(t.Text).IsEqualTo("a, b");
	}

	[Test]
	public async Task Equality_IsTextOnly()
	{
		var plain = MarkupText.Plain("x");
		var styled = MarkupText.Wrap(new Tag("b"), "x");
		await Assert.That(plain.Equals(styled)).IsTrue();
		await Assert.That(plain.GetHashCode()).IsEqualTo(styled.GetHashCode());
		await Assert.That(plain.TextEquals("x")).IsTrue();
		await Assert.That(plain.Equals((object)"x")).IsFalse();
	}

	// Serialised against MarkupSet_StillInternsAfterTheTableFills: that test drops the intern table,
	// and a drop landing between the two Of() calls here would break reference equality legitimately.
	[Test, NotInParallel]
	public async Task MarkupSet_IsValueEqualAndInterned()
	{
		var a = MarkupSet.Of(new Tag("b"));
		var b = MarkupSet.Of(new Tag("b"));
		await Assert.That(a).IsEqualTo(b);
		await Assert.That(ReferenceEquals(a, b)).IsTrue();
	}

	/// <summary>
	/// The intern table is bounded and drops everything when it fills. That path is reached by
	/// creating more distinct sets than it holds, which is what this does — and interning has to
	/// keep working across the drop, which is the part a broken approximate count would break (a
	/// count that never resets clears the table on every later miss, so no two equal sets built
	/// after the first drop would ever share an instance again).
	/// </summary>
	[Test, NotInParallel]
	public async Task MarkupSet_StillInternsAfterTheTableFills()
	{
		// Comfortably past the 4,096-entry cap, so the clear happens whichever set ran first.
		for (var i = 0; i < 5000; i++) MarkupSet.Of(new Tag($"fill-{i}"));

		var a = MarkupSet.Of(new Tag("after-the-drop"));
		// A distinct set created in between proves the entry survives being shouldered aside by
		// another insertion, not just that two calls made back to back happened to line up.
		var c = MarkupSet.Of(new Tag("distinct-after-the-drop"));
		var aAgain = MarkupSet.Of(new Tag("after-the-drop"));

		await Assert.That(aAgain).IsEqualTo(a);
		await Assert.That(ReferenceEquals(a, aAgain)).IsTrue();
		await Assert.That(ReferenceEquals(a, c)).IsFalse();

		// Exceeding the cap a second time must not throw (e.g. from a stale/negative approximate count).
		for (var i = 0; i < 5000; i++) MarkupSet.Of(new Tag($"fill2-{i}"));
	}

	[Test]
	public async Task ToString_IsPlainText()
		=> await Assert.That(MarkupText.Wrap(new Tag("b"), "x").ToString()).IsEqualTo("x");
}
