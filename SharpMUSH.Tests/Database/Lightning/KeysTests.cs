using System.Text;
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

	/// <summary>
	/// The buffer form of every key shape spells the same bytes as the array form; a read may use
	/// either and land on the same entry.
	/// </summary>
	[Test]
	public async Task BufferKeysMatchArrayKeysByteForByte()
	{
		var buffer = new byte[Keys.StackKeySize];
		await Assert.That(Keys.Dbref(42, buffer).ToArray()).IsEquivalentTo(Keys.Dbref(42));
		await Assert.That(Keys.Str("Widget", buffer).ToArray()).IsEquivalentTo(Keys.Str("Widget"));
		await Assert.That(Keys.Upper("desc`Format", buffer).ToArray()).IsEquivalentTo(Keys.Upper("desc`Format"));
		await Assert.That(Keys.Lower("Bob The Builder", buffer).ToArray()).IsEquivalentTo(Keys.Lower("Bob The Builder"));
		await Assert.That(Keys.Attr(42, "desc`Format", buffer).ToArray()).IsEquivalentTo(Keys.Attr(42, "desc`Format"));
		await Assert.That(Keys.AttrPrefix(42, buffer).ToArray()).IsEquivalentTo(Keys.AttrPrefix(42));
		await Assert.That(Keys.AttrPrefix(42, "desc`", buffer).ToArray()).IsEquivalentTo(Keys.AttrPrefix(42, "desc`"));
		await Assert.That(Keys.Composite(7, "public", buffer).ToArray()).IsEquivalentTo(Keys.Composite(7, "public"));
		await Assert.That(Keys.Composite("public", 7, buffer).ToArray()).IsEquivalentTo(Keys.Composite("public", 7));
		await Assert.That(Keys.Composite("a", "b", buffer).ToArray()).IsEquivalentTo(Keys.Composite("a", "b"));
		await Assert.That(Keys.Composite("a", "b", "c", buffer).ToArray()).IsEquivalentTo(Keys.Composite("a", "b", "c"));
		await Assert.That(Keys.Composite("a", "b", 258u, buffer).ToArray()).IsEquivalentTo(Keys.Composite("a", "b", 258u));
	}

	/// <summary>
	/// Every key shape is the plain concatenation of its parts: an eight-byte big-endian dbref, a
	/// single zero separator, UTF-8 text (case-folded with the invariant culture where the shape says
	/// so) and a four-byte big-endian number. Spelled out here by hand so the builder cannot drift.
	/// </summary>
	[Test]
	public async Task KeysAreThePlainConcatenationOfTheirParts()
	{
		static byte[] Be64(long value) => [.. BitConverter.GetBytes(value).Reverse()];
		static byte[] Be32(uint value) => [.. BitConverter.GetBytes(value).Reverse()];
		static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
		byte[] sep = [0];

		await Assert.That(Keys.Dbref(-1)).IsEquivalentTo(Be64(-1));
		await Assert.That(Keys.Str("Ünïcode")).IsEquivalentTo(Utf8("Ünïcode"));
		await Assert.That(Keys.Upper("desc`ünïcode")).IsEquivalentTo(Utf8("DESC`ÜNÏCODE"));
		await Assert.That(Keys.Lower("Bob The BÜILDER")).IsEquivalentTo(Utf8("bob the büilder"));
		await Assert.That(Keys.Attr(42, "desc`Format")).IsEquivalentTo([.. Be64(42), .. sep, .. Utf8("DESC`FORMAT")]);
		await Assert.That(Keys.AttrPrefix(42)).IsEquivalentTo([.. Be64(42), .. sep]);
		await Assert.That(Keys.AttrPrefix(42, "desc`")).IsEquivalentTo([.. Be64(42), .. sep, .. Utf8("DESC`")]);
		await Assert.That(Keys.Composite(7, "Public")).IsEquivalentTo([.. Be64(7), .. sep, .. Utf8("Public")]);
		await Assert.That(Keys.Composite("Public", 7)).IsEquivalentTo([.. Utf8("Public"), .. sep, .. Be64(7)]);
		await Assert.That(Keys.Composite("a", "B")).IsEquivalentTo([.. Utf8("a"), .. sep, .. Utf8("B")]);
		await Assert.That(Keys.Composite("a", "B", "c")).IsEquivalentTo([.. Utf8("a"), .. sep, .. Utf8("B"), .. sep, .. Utf8("c")]);
		await Assert.That(Keys.Composite("a", "B", 258u)).IsEquivalentTo([.. Utf8("a"), .. sep, .. Utf8("B"), .. sep, .. Be32(258u)]);
		await Assert.That(Keys.Concat(Keys.Dbref(1), Keys.Sep, Keys.Dbref(2))).IsEquivalentTo([.. Be64(1), .. sep, .. Be64(2)]);
	}

	[Test]
	public async Task CompositeKeysWithANumberEndInItBigEndian()
	{
		var key = Keys.Composite("a", "b", 258u);
		await Assert.That(key).IsEquivalentTo(new byte[] { (byte)'a', 0, (byte)'b', 0, 0, 0, 1, 2 });
	}

	/// <summary>
	/// The buffer is a fast path, not a limit: a key that does not fit is still spelled correctly,
	/// it just is not written into the buffer the caller handed over.
	/// </summary>
	[Test]
	public async Task AKeyLongerThanItsBufferSpillsToTheHeapIntact()
	{
		var longName = new string('x', Keys.StackKeySize * 2);
		var buffer = new byte[Keys.StackKeySize];

		var key = Keys.Attr(42, longName, buffer).ToArray();

		await Assert.That(key).IsEquivalentTo(Keys.Attr(42, longName));
		await Assert.That(key.Length).IsEqualTo(9 + longName.Length);
		await Assert.That(Keys.ParseAttr(key).LongName).IsEqualTo(longName.ToUpperInvariant());
		await Assert.That(buffer.All(b => b == 0)).IsFalse().Because("the dbref and separator were written before the key outgrew the buffer");
	}

	[Test]
	public async Task AnEmptyBufferStillYieldsTheKey()
		=> await Assert.That(Keys.Attr(5, "FOO", []).ToArray()).IsEquivalentTo(Keys.Attr(5, "FOO"));

	/// <summary>
	/// Case folding can change the UTF-8 length — dotless i is two bytes and its upper case one —
	/// so the builder measures the folded text, not the input.
	/// </summary>
	[Test]
	public async Task CaseFoldingIsMeasuredAfterFolding()
	{
		const string name = "ıstanbul";
		var buffer = new byte[Keys.StackKeySize];

		var upper = Keys.Upper(name, buffer).ToArray();
		var lower = Keys.Lower("İSTANBUL", buffer).ToArray();

		await Assert.That(Keys.ReadStr(upper)).IsEqualTo(name.ToUpperInvariant());
		await Assert.That(Keys.ReadStr(lower)).IsEqualTo("İSTANBUL".ToLowerInvariant());
		await Assert.That(upper).IsEquivalentTo(Keys.Upper(name));
	}
}
