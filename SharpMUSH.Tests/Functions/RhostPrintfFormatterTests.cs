using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// printf()'s RhostMUSH grammar (<c>rhost_printf</c>). Every expected value is a live RhostMUSH 5.11.2's
/// answer to the same format and values, its CR LF line ends written as a newline.
/// </summary>
public class RhostPrintfFormatterTests
{
	[Test]
	[Arguments("|$5s|", "abcdefgh", "|abcde|")]
	[Arguments("|$-5s|", "abcdefgh", "|abcde|")]
	[Arguments("|$*5s|", "abcdefgh", "|defgh|")]
	[Arguments("|$+5s|", "abcdefgh", "|abcdefgh|")]
	[Arguments("|$^6s|", "abc", "| abc  |")]
	[Arguments("|$05s|", "12", "|00012|")]
	[Arguments("|$-05s|", "12", "|12   |")]
	[Arguments("|$_12s|", "a b c", "|a     b    c|")]
	[Arguments("|$_12s|", "abc", "|    abc     |")]
	[Arguments("|$10:.:s|", "ab", "|........ab|")]
	[Arguments("$15:x:sx", "Walker", "xxxxxxxxxWalkerx")]
	[Arguments("$x5s", "ab", "   ab")]
	[Arguments("$5s$", "ab", "   ab")]
	[Arguments("a$$b", "x", "a$b")]
	[Arguments("$5", "ab", "")]
	[Arguments("<$-5|s>", "abcdefghijkl", "<abcde>\n fghij\n kl   ")]
	[Arguments("<$5|s>", "abcdefghijkl", "<abcde>\n fghij\n    kl")]
	[Arguments("<$-5|s>", "ab cdefghij", "<ab cd>\n efghi\n j    ")]
	[Arguments("<$-7|\"s>", "aaa bbb ccc dddddddddd", "<aaa    >\n bbb    \n ccc    \n ddddddd\n ddd    ")]
	[Arguments("<$-7|\"s>", "aa bb ccccc", "<aa bb  >\n ccccc  ")]
	[Arguments("<$-7|\"s>", "a b c d e f g h i", "<a b c  >\n d e f  \n g h i  ")]
	[Arguments("<$-8|\"s>", "aaa bbb ccc", "<aaa bbb >\n ccc     ")]
	[Arguments("<$-12_s>", "a b c d", RhostPrintfFormatter.AlignmentRepeated)]
	public async Task OneField(string format, string value, string expected)
		=> await Assert.That(Format(format, value)).IsEqualTo(expected);

	[Test]
	[Arguments("|$s|$s|", new[] { "a" }, "|a||")]
	[Arguments("|$s|", new[] { "a", "b" }, "|a|")]
	[Arguments("|$!-5s|$-5s|", new[] { "", "b" }, "|b    |")]
	[Arguments("|$-5s|$-5s|", new[] { "", "b" }, "|     |b    |")]
	[Arguments("A$!-3sB$-3sC", new[] { "", "x" }, "Ax  C")]
	[Arguments("A$-3sB$!-3sC$-2sD", new[] { "y", "", "z" }, "Ay  Bz D")]
	[Arguments("<$&-5s|$-3s>", new[] { "a\nbb\nc", "X" }, "<a    |X  >\n bb   \n c    ")]
	[Arguments("<$&-5s|$&-3s>", new[] { "a\nbb\nc", "X\nY" }, "<a    |X  >\n bb    Y  \n c        ")]
	[Arguments("<$&5s|$&-3s>", new[] { "a\nbb\nc", "X\nY" }, "<    a|X  >\n    bb Y  \n     c    ")]
	[Arguments("<$&-5:.:s|$&-3s>", new[] { "a\nbb\nc", "X\nY" }, "<a....|X  >\n bb... Y  \n c....    ")]
	[Arguments("<$&-3s|$&-3s>", new[] { "a\n", "b\n" }, "<a  |b  >\n        ")]
	[Arguments("<$.&-3s|$.&-3s>", new[] { "a\n", "b\n" }, "<a  |b  >")]
	public async Task SeveralFields(string format, string[] values, string expected)
		=> await Assert.That(Format(format, values)).IsEqualTo(expected);

	/// <summary>
	/// <c>|"</c> with an indent, worked out from RhostMUSH's source (<c>showfield_printf</c>): the cell limit
	/// counts the value's own characters, and a line after a newline has the indent taken off its width.
	/// </summary>
	[Test]
	[Arguments("<$-4/8/|\";1;s>", "abcdefghij", "<abcd>\n  efg\n  h  ")]
	[Arguments("<$-5|\";2;s>", "abcde\nfghij", "<abcde>\n   fgh\n   ij ")]
	public async Task WrappedWithAnIndent(string format, string value, string expected)
		=> await Assert.That(Format(format, value)).IsEqualTo(expected);

	/// <summary>
	/// <c>;N+W;</c>: the rows after the first are W wider, so an indent of N takes nothing from the text when W
	/// is N. Worked out from RhostMUSH's help and source; no live server answered these.
	/// </summary>
	[Test]
	[Arguments("<$-10|\";4+4;s>", "aaaa bbbb cccc dddd eeee ffff", "<aaaa bbbb >\n     cccc dddd \n     eeee ffff ")]
	[Arguments("<$-10|\";4+2;s>", "aaaa bbbb cccc dddd", "<aaaa bbbb >\n     cccc    \n     dddd    ")]
	[Arguments("<$-10|\";4.1+4;s>", "aaaa bbbb cccc dddd eeee ffff", "<aaaa bbbb >\n cccc dddd     \n     eeee ffff ")]
	public async Task WrappedWithAWidenedIndent(string format, string value, string expected)
		=> await Assert.That(Format(format, value)).IsEqualTo(expected);

	[Test]
	public async Task AWidenedIndentKeepsTheFieldsAfterItOnTheFirstRow()
		=> await Assert.That(Format("<$-10|\";4+4;s|$-2s>", "aaaa bbbb cccc dddd", "X")).IsEqualTo("<aaaa bbbb |X >\n     cccc dddd ");

	private static string Format(string format, params string[] values)
		=> RhostPrintfFormatter.TryFormat(MarkupText.Plain(format), values.Select(MarkupText.Plain).ToArray(),
			out var result, out var error)
			? result.ToPlainText()
			: error!;
}
