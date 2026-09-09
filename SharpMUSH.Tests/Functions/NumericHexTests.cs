using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Functions;

public class NumericHexTests
{
	[Test]
	[Arguments("0x1p0", 1d)]
	[Arguments("-0x1.8p2", -6d)]
	[Arguments("0x1p-1074", double.Epsilon)]
	[Arguments("0x1p-1075", 0d)]
	[Arguments("0x1.0000000000001p-1075", double.Epsilon)]
	[Arguments("0x1.fffffffffffffp1023", double.MaxValue)]
	[Arguments("0x1p1024", double.PositiveInfinity)]
	[Arguments("0x1.00000000000008p0", 1d)]
	[Arguments("0x1.0000000000000800000000000001p0", 1.0000000000000002d)]
	[Arguments("0x1.00000000000018p0", 1.0000000000000004d)]
	public async Task HexadecimalRounding(string text, double expected)
	{
		var valid = new NumericEvaluation(false, true).TryDouble(text, out var actual);
		await Assert.That(valid).IsTrue();
		await Assert.That(BitConverter.DoubleToInt64Bits(actual)).IsEqualTo(BitConverter.DoubleToInt64Bits(expected));
	}

	[Test]
	public async Task DecimalConversionPreservesAnExactlyRepresentableLargeInteger()
	{
		var valid = new NumericEvaluation(false, true).TryDecimal("0x10000000000001", out var actual);
		await Assert.That(valid).IsTrue();
		await Assert.That(actual).IsEqualTo(4503599627370497m);
	}

	[Test]
	[Arguments("0x1junk")]
	[Arguments("0x1p+")]
	[Arguments("0x.p2")]
	[Arguments("0x1 ")]
	public async Task StrictRejectsIncompleteOrTrailingText(string text)
		=> await Assert.That(new NumericEvaluation(false, true).TryDouble(text, out _)).IsFalse();
}
