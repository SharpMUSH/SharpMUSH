using SharpMUSH.Configuration.Generated;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Covers <see cref="Emit"/>, which turns constants read out of the compilation into C# source for the
/// config generators. Two failure modes matter and neither is visible from generated output today: a
/// constant type with no case is silently emitted as <c>null</c>, dropping a declared default or bound;
/// and an unescaped control character produces source that will not compile.
/// </summary>
public class EmitTests
{
	[Test]
	[Arguments((sbyte)-8, "(sbyte)-8")]
	[Arguments((byte)8, "(byte)8")]
	[Arguments((short)-16, "(short)-16")]
	[Arguments((ushort)16, "(ushort)16")]
	[Arguments(32, "32")]
	[Arguments(32u, "32u")]
	[Arguments(64L, "64L")]
	[Arguments(64UL, "64UL")]
	[Arguments(true, "true")]
	[Arguments(false, "false")]
	public async Task Literal_EmitsEveryIntegralConstantWithItsType(object value, string expected)
	{
		await Assert.That(Emit.Literal(value)).IsEqualTo(expected);
	}

	/// <summary>
	/// Min and Max are declared object?, so a bound must round-trip as its own type — a float written as a
	/// bare number would come back a double and compare unequal to the value it bounds.
	/// </summary>
	[Test]
	public async Task Literal_KeepsFloatingPointConstantsDistinguishable()
	{
		await Assert.That(Emit.Literal(1.5d)).IsEqualTo("1.5d");
		await Assert.That(Emit.Literal(1.5f)).IsEqualTo("1.5f");
		await Assert.That(Emit.Literal(1.5m)).IsEqualTo("1.5m");
	}

	[Test]
	public async Task Literal_EmitsNullForNoValue()
	{
		await Assert.That(Emit.Literal(null)).IsEqualTo("null");
	}

	[Test]
	[Arguments("plain", "\"plain\"")]
	[Arguments("say \"hi\"", "\"say \\\"hi\\\"\"")]
	[Arguments("back\\slash", "\"back\\\\slash\"")]
	[Arguments("two\nlines", "\"two\\nlines\"")]
	[Arguments("carriage\rreturn", "\"carriage\\rreturn\"")]
	[Arguments("tab\there", "\"tab\\there\"")]
	public async Task Literal_EscapesStringsSoTheLiteralStaysOnOneLine(string value, string expected)
	{
		await Assert.That(Emit.Literal(value)).IsEqualTo(expected);
	}

	[Test]
	[Arguments('+', "'+'")]
	[Arguments('\'', "'\\''")]
	[Arguments('\\', "'\\\\'")]
	[Arguments('\n', "'\\n'")]
	public async Task Literal_EscapesCharacterConstants(char value, string expected)
	{
		await Assert.That(Emit.Literal(value)).IsEqualTo(expected);
	}

	/// <summary>
	/// A separator C# rejects inside a literal has to become an escape, not pass through. These are not
	/// System.Char control characters, so char.IsControl alone would let them through.
	/// </summary>
	[Test]
	public async Task Escape_EncodesSeparatorsThatCannotAppearInALiteral()
	{
		await Assert.That(Emit.Escape("a" + (char)0x2028 + "b")).IsEqualTo("a\\u2028b");
		await Assert.That(Emit.Escape("a" + (char)0x2029 + "b")).IsEqualTo("a\\u2029b");
		await Assert.That(Emit.Escape("a" + (char)0x0085 + "b")).IsEqualTo("a\\u0085b");
	}

	[Test]
	public async Task Quote_EmitsNullForNoValue()
	{
		await Assert.That(Emit.Quote(null)).IsNull();
	}
}
