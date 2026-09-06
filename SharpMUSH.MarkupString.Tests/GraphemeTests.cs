public class GraphemeTests
{
	/// <summary>An "a", a surrogate pair (U+1F600) and a "b". Index 2 sits inside the pair.</summary>
	private const string Emoji = "a\U0001F600b";

	/// <summary>An "e" plus COMBINING ACUTE ACCENT, so index 1 sits inside the cluster.</summary>
	private const string Decomposed = "e\u0301";

	/// <summary>Three emoji joined by ZERO WIDTH JOINER; one cluster, 8 UTF-16 units.</summary>
	private const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";

	[Test]
	public async Task SnapStart_InsideSurrogatePair_MovesDown()
		=> await Assert.That(Graphemes.SnapStart(Emoji, 2)).IsEqualTo(1);

	[Test]
	public async Task SnapEnd_InsideSurrogatePair_MovesUp()
		=> await Assert.That(Graphemes.SnapEnd(Emoji, 2)).IsEqualTo(3);

	[Test]
	public async Task IsBoundary_InsideCombiningCluster_IsFalse()
		=> await Assert.That(Graphemes.IsBoundary(Decomposed, 1)).IsFalse();

	[Test]
	public async Task IsBoundary_AtClusterEdges_IsTrue()
	{
		await Assert.That(Graphemes.IsBoundary(Decomposed, 0)).IsTrue();
		await Assert.That(Graphemes.IsBoundary(Decomposed, 2)).IsTrue();
		await Assert.That(Graphemes.IsBoundary(Emoji, 1)).IsTrue();
		await Assert.That(Graphemes.IsBoundary(Emoji, 2)).IsFalse();
		await Assert.That(Graphemes.IsBoundary(Emoji, 3)).IsTrue();
	}

	[Test]
	public async Task AsciiIndices_AreUnchanged()
	{
		for (var i = 0; i <= 5; i++)
		{
			await Assert.That(Graphemes.SnapStart("abcde", i)).IsEqualTo(i);
			await Assert.That(Graphemes.SnapEnd("abcde", i)).IsEqualTo(i);
			await Assert.That(Graphemes.IsBoundary("abcde", i)).IsTrue();
		}
	}

	[Test]
	public async Task OutOfRangeIndices_AreClamped()
	{
		await Assert.That(Graphemes.SnapStart(Emoji, -5)).IsEqualTo(0);
		await Assert.That(Graphemes.SnapEnd(Emoji, -5)).IsEqualTo(0);
		await Assert.That(Graphemes.SnapStart(Emoji, 99)).IsEqualTo(4);
		await Assert.That(Graphemes.SnapEnd(Emoji, 99)).IsEqualTo(4);
		await Assert.That(Graphemes.SnapStart("", 0)).IsEqualTo(0);
	}

	[Test]
	public async Task CrLf_IsOneCluster()
	{
		await Assert.That(Graphemes.IsBoundary("a\r\nb", 2)).IsFalse();
		await Assert.That(Graphemes.SnapStart("a\r\nb", 2)).IsEqualTo(1);
		await Assert.That(Graphemes.SnapEnd("a\r\nb", 2)).IsEqualTo(3);
	}

	[Test]
	public async Task ZwjSequence_IsOneCluster()
	{
		await Assert.That(Graphemes.SnapStart(Family, 5)).IsEqualTo(0);
		await Assert.That(Graphemes.SnapEnd(Family, 5)).IsEqualTo(Family.Length);
	}
}
