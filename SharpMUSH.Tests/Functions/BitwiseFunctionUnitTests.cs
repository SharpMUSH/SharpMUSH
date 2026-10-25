using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class BitwiseFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("10", "10", "2", "1010")]
	[Arguments("10", "2", "10", "2")]
	[Arguments("woof", "64", "32", "c52gv")]
	[Arguments("oof", "32", "64", "GMP")]
	[Arguments("woof", "1", "10", "#-1 ARGUMENT 1 MUST BE BETWEEN 2 AND 64")]
	[Arguments("woof", "10", "1", "#-1 ARGUMENT 2 MUST BE BETWEEN 2 AND 64")]
	[Arguments("woof", "10", "65", "#-1 ARGUMENT 2 MUST BE BETWEEN 2 AND 64")]
	[Arguments("woof", "65", "10", "#-1 ARGUMENT 1 MUST BE BETWEEN 2 AND 64")]
	public async Task BaseConv(string number, string frombase, string tobase, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain($"baseconv({number},{frombase},{tobase})"));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("band(6,3)", "2")]
	[Arguments("band(5,3)", "1")]
	[Arguments("band(12,10)", "8")]
	public async Task Band(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bor(6,3)", "7")]
	[Arguments("bor(5,3)", "7")]
	[Arguments("bor(12,10)", "14")]
	public async Task Bor(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bxor(6,3)", "5")]
	[Arguments("bxor(5,3)", "6")]
	[Arguments("bxor(12,10)", "6")]
	public async Task Bxor(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("shl(1,3)", "8")]
	[Arguments("shl(5,2)", "20")]
	public async Task Shl(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("shr(8,3)", "1")]
	[Arguments("shr(20,2)", "5")]
	public async Task Shr(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bnand(12,10)", "4")]
	[Arguments("bnand(6,3)", "4")]
	[Arguments("bnand(5,3)", "4")]
	public async Task Bnand(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bnot(0)", "-1")]
	[Arguments("bnot(1)", "-2")]
	public async Task Bnot(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// Bitwise functions operate on 64-bit unsigned integers, matching PennMUSH's UIVAL.
	/// Expected values were read off a live PennMUSH 1.8.8 oracle.
	/// </summary>
	[Test]
	[Arguments("band(4294967296,4294967296)", "4294967296")]
	[Arguments("bor(4294967296,1)", "4294967297")]
	[Arguments("bxor(4294967296,1)", "4294967297")]
	[Arguments("bxor(12884901888,4294967296)", "8589934592")]
	[Arguments("shl(1,40)", "1099511627776")]
	[Arguments("shl(1,62)", "4611686018427387904")]
	[Arguments("shr(4294967296,32)", "1")]
	public async Task BitwiseOperationsAre64Bit(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// Results with the high bit set are rendered signed. PennMUSH renders them signed too, but its
	/// output breaks down past 2^63 and emits a bare "-" for these; a real number is printed instead.
	/// </summary>
	[Test]
	[Arguments("bor(9223372036854775808,0)", "-9223372036854775808")]
	[Arguments("shl(1,63)", "-9223372036854775808")]
	[Arguments("band(18446744073709551615,18446744073709551615)", "-1")]
	[Arguments("shr(18446744073709551615,1)", "9223372036854775807")]
	public async Task ResultsAboveSignedRangeArePrintedSigned(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// Shift counts are taken modulo 64. PennMUSH inherits this from the x86 shift instruction;
	/// C# specifies it, so the two agree for every count, including counts that do not fit an int.
	/// </summary>
	[Test]
	[Arguments("shl(1,64)", "1")]
	[Arguments("shl(1,100)", "68719476736")]
	[Arguments("shl(1,4294967296)", "1")]
	[Arguments("shr(1,64)", "1")]
	public async Task ShiftCountsWrapAtSixtyFour(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bnand(12,10,5)", "#-1 FUNCTION (BNAND) EXPECTS 2 ARGUMENTS BUT GOT 3")]
	public async Task BnandTakesExactlyTwoArguments(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("band(-1,1)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	[Arguments("shl(-1,1)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	[Arguments("bnot(1.5)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments("bnot(-1)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	public async Task BitwiseRejectsNonUnsignedIntegers(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>Each answer is what TinyMUX 2.14 gave for the same call; RhostMUSH 5.13 agrees on all but the invalid digits.</summary>
	[Test]
	[Arguments("pack(255,16)", "FF")]
	[Arguments("pack(255)", "3$")]
	[Arguments("pack(-255,16)", "-FF")]
	[Arguments("pack(0,16)", "0")]
	[Arguments("pack(64)", "10")]
	[Arguments("pack(10,2)", "1010")]
	[Arguments("pack(-10,2)", "-1010")]
	[Arguments("pack(61,62)", "z")]
	[Arguments("pack(12345678901234,16)", "B3A73CE2FF2")]
	[Arguments("pack(9223372036854775807,16)", "7FFFFFFFFFFFFFFF")]
	[Arguments("pack(255,16,1)", "ff")]
	[Arguments("pack(255,16,0)", "FF")]
	[Arguments("pack(61,62,1)", "9")]
	[Arguments("pack(-255,16,1)", "-ff")]
	[Arguments("pack(-1,64)", "#-1 NEGATIVE NUMBER IS NOT REPRESENTABLE IN OUTPUT RADIX")]
	[Arguments("pack(1,1)", "#-1 RADIX MUST BE A NUMBER BETWEEN 2 and 64")]
	[Arguments("pack(1,65)", "#-1 RADIX MUST BE A NUMBER BETWEEN 2 and 64")]
	[Arguments("pack(abc,16)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments("pack(1.5,16)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments("pack(5,16.5)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments("pack(255,)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments("unpack(FF,16)", "255")]
	[Arguments("unpack(10)", "64")]
	[Arguments("unpack(-FF,16)", "-255")]
	[Arguments("unpack(@,64)", "62")]
	[Arguments("unpack($,64)", "63")]
	[Arguments("unpack(zz,64)", "3965")]
	[Arguments("unpack(Z,36)", "35")]
	[Arguments("unpack(,16)", "0")]
	[Arguments("unpack(+5,10)", "5")]
	[Arguments("unpack(ff,16,1)", "255")]
	[Arguments("unpack(z,36,1)", "35")]
	[Arguments("unpack(ff,16)", "#-1 NUMBER IS NOT VALID FOR INPUT RADIX")]
	[Arguments("unpack(G,16)", "#-1 NUMBER IS NOT VALID FOR INPUT RADIX")]
	[Arguments("unpack(FF,16,1)", "#-1 NUMBER IS NOT VALID FOR INPUT RADIX")]
	[Arguments("unpack(1,1)", "#-1 RADIX MUST BE A NUMBER BETWEEN 2 and 64")]
	[Arguments("unpack(FF,)", "#-1 RADIX MUST BE A NUMBER BETWEEN 2 and 64")]
	public async Task PackAndUnpack(string str, string expected)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain(str));
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("FFFFFFFFFFFFFFFFFFFF", "16")]
	[Arguments("zzzzzzzzzzzzzzzzzzzz", "64")]
	[Arguments("-101010101010101010101010101010101010101010101010101010101010101010", "2")]
	public async Task UnpackThenPackGivesTheNumberBack(string text, string radix)
	{
		var result = await Parser.EvaluateAsync(MarkupText.Plain($"pack(unpack({text},{radix}),{radix})"));
		await Assert.That(result.ToPlainText()).IsEqualTo(text);
	}
}
