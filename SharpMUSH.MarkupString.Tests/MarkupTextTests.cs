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

	[Test]
	public async Task MarkupSet_IsValueEqualAndInterned()
	{
		var a = MarkupSet.Of(new Tag("b"));
		var b = MarkupSet.Of(new Tag("b"));
		await Assert.That(a).IsEqualTo(b);
		await Assert.That(ReferenceEquals(a, b)).IsTrue();
	}

	[Test]
	public async Task ToString_IsPlainText()
		=> await Assert.That(MarkupText.Wrap(new Tag("b"), "x").ToString()).IsEqualTo("x");
}
