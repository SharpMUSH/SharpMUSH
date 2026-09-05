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
		var result = await Parser.FunctionParse(MModule.single($"baseconv({number},{frombase},{tobase})"));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("band(6,3)", "2")]
	[Arguments("band(5,3)", "1")]
	[Arguments("band(12,10)", "8")]
	public async Task Band(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bor(6,3)", "7")]
	[Arguments("bor(5,3)", "7")]
	[Arguments("bor(12,10)", "14")]
	public async Task Bor(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bxor(6,3)", "5")]
	[Arguments("bxor(5,3)", "6")]
	[Arguments("bxor(12,10)", "6")]
	public async Task Bxor(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("shl(1,3)", "8")]
	[Arguments("shl(5,2)", "20")]
	public async Task Shl(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("shr(8,3)", "1")]
	[Arguments("shr(20,2)", "5")]
	public async Task Shr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bnand(12,10)", "4")]
	[Arguments("bnand(6,3)", "4")]
	[Arguments("bnand(5,3)", "4")]
	public async Task Bnand(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bnot(0)", "-1")]
	[Arguments("bnot(1)", "-2")]
	public async Task Bnot(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
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
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bnand(12,10,5)", "#-1 FUNCTION (BNAND) EXPECTS AT MOST 2 ARGUMENTS BUT GOT 3")]
	public async Task BnandTakesExactlyTwoArguments(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("band(-1,1)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	[Arguments("shl(-1,1)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	[Arguments("bnot(1.5)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments("bnot(-1)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	public async Task BitwiseRejectsNonUnsignedIntegers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
