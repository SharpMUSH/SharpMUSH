using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Tests.Database.Lightning;

public class KeysTests
{
	[Test]
	public async Task DbrefKeysSortNumerically()
	{
		var k9 = Keys.Dbref(9);
		var k10 = Keys.Dbref(10);
		var k256 = Keys.Dbref(256);
		await Assert.That(k9.AsSpan().SequenceCompareTo(k10)).IsLessThan(0);
		await Assert.That(k10.AsSpan().SequenceCompareTo(k256)).IsLessThan(0);
		await Assert.That(Keys.ReadDbref(k256)).IsEqualTo(256);
	}

	[Test]
	public async Task AttributeKeysGroupByDbrefAndSortByLongName()
	{
		var a = Keys.Attr(5, "FOO");
		var b = Keys.Attr(5, "FOO`BAR");
		var c = Keys.Attr(6, "AAA");
		await Assert.That(a.AsSpan().SequenceCompareTo(b)).IsLessThan(0);
		await Assert.That(b.AsSpan().SequenceCompareTo(c)).IsLessThan(0);
		await Assert.That(Keys.StartsWith(b, Keys.AttrPrefix(5))).IsTrue();
		await Assert.That(Keys.StartsWith(c, Keys.AttrPrefix(5))).IsFalse();
		await Assert.That(Keys.StartsWith(b, Keys.AttrPrefix(5, "FOO`"))).IsTrue();
	}

	[Test]
	public async Task AttributeKeysAreUpperCasedAndRoundTrip()
	{
		var key = Keys.Attr(42, "desc`Format");
		var (dbref, longName) = Keys.ParseAttr(key);
		await Assert.That(dbref).IsEqualTo(42);
		await Assert.That(longName).IsEqualTo("DESC`FORMAT");
	}

	[Test]
	public async Task CompositeKeysSeparateWithZeroByte()
	{
		var key = Keys.Composite("public", 7);
		await Assert.That(key[6]).IsEqualTo((byte)0);
		await Assert.That(Keys.ReadDbref(key.AsSpan(7))).IsEqualTo(7);
	}
}
