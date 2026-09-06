using System.Text;

public class DisplayWidthTests
{
	[Test]
	public async Task Of_Ascii_IsOneCellPerCharacter()
		=> await Assert.That(DisplayWidth.Of("abc")).IsEqualTo(3);

	[Test]
	public async Task Of_EastAsianWide_IsTwoCellsPerCharacter()
		=> await Assert.That(DisplayWidth.Of("\u65E5\u672C")).IsEqualTo(4);

	[Test]
	public async Task Of_CombiningMark_CountsAsZero()
	{
		await Assert.That(DisplayWidth.Of("\u00E9")).IsEqualTo(1);
		await Assert.That(DisplayWidth.Of("e\u0301")).IsEqualTo(1);
	}

	[Test]
	public async Task Of_ZwjEmojiSequence_CountsOnce()
		=> await Assert.That(DisplayWidth.Of("\U0001F468\u200D\U0001F469\u200D\U0001F467")).IsEqualTo(2);

	[Test]
	public async Task Of_Empty_IsZero()
		=> await Assert.That(DisplayWidth.Of("")).IsEqualTo(0);

	[Test]
	public async Task Of_HalfwidthKatakana_IsOneCell()
		=> await Assert.That(DisplayWidth.Of("\uFF71")).IsEqualTo(1);

	[Test]
	public async Task Of_FullwidthLatin_IsTwoCells()
		=> await Assert.That(DisplayWidth.Of("\uFF21")).IsEqualTo(2);

	[Test]
	public async Task OfRune_ClassifiesControlsMarksAndJamoAsZero()
	{
		await Assert.That(DisplayWidth.OfRune(new Rune('\u0007'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u009B'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u0301'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u200D'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u1160'))).IsEqualTo(0);
		await Assert.That(DisplayWidth.OfRune(new Rune('a'))).IsEqualTo(1);
		await Assert.That(DisplayWidth.OfRune(new Rune('\u65E5'))).IsEqualTo(2);
	}

	[Test]
	public async Task IndexAtWidth_StopsBeforeSplittingAWideCharacter()
	{
		await Assert.That(DisplayWidth.IndexAtWidth("\u65E5\u672C\u8A9E", 5)).IsEqualTo(2);
		await Assert.That(DisplayWidth.IndexAtWidth("\u65E5\u672C\u8A9E", 6)).IsEqualTo(3);
		await Assert.That(DisplayWidth.IndexAtWidth("\u65E5\u672C\u8A9E", 0)).IsEqualTo(0);
		await Assert.That(DisplayWidth.IndexAtWidth("abcde", 3)).IsEqualTo(3);
		await Assert.That(DisplayWidth.IndexAtWidth("abc", 99)).IsEqualTo(3);
	}

	[Test]
	public async Task IndexAtWidth_KeepsCombiningMarkWithItsBase()
		=> await Assert.That(DisplayWidth.IndexAtWidth("e\u0301x", 1)).IsEqualTo(2);
}
