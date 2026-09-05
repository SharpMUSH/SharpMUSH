using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class MathFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("abs(-1)", "1")]
	[Arguments("abs(-1.5)", "1.5")]
	[Arguments("abs(1)", "1")]
	[Arguments("abs(0)", "0")]
	[Arguments("abs(-0)", "0")]
	[Arguments("abs(99999999999)", "99999999999")]
	[Arguments("abs(-99999999999)", "99999999999")]
	public async Task Abs(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cos(90,d)", "0")]
	[Arguments("cos(pi(),r)", "-1")]
	[Arguments("cos(pi())", "-1")]
	public async Task Cos(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("acos(cos(90,d),d)", "90")]
	[Arguments("acos(cos(1,r))", "1")]
	[Arguments("acos(cos(1,r),r)", "1")]
	public async Task Acos(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sin(90,d)", "1")]
	[Arguments("sin(pi(),r)", "0")]
	[Arguments("sin(pi())", "0")]
	public async Task Sin(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("asin(sin(90,d),d)", "90")]
	public async Task Asin(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("tan(45,d)", "1")]
	[Arguments("tan(90, d)", "#-1 OUT OF RANGE")]
	public async Task Tan(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("atan(tan(45,d),d)", "45")]
	[Arguments("atan(tan(1,r))", "1")]
	[Arguments("atan(tan(1,r),r)", "1")]
	public async Task Atan(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("atan2(0, -1)", "3.14159265358979")]
	[Arguments("atan2(0, 1)", "0")]
	[Arguments("atan2(-0.0001, 0)", "-1.5707963267949")]
	[Arguments("atan2(0.0001, 0)", "1.5707963267949")]
	public async Task Atan2(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ctu(90,d,r)", "1.5707963267949")]
	[Arguments("ctu(pi(),r,d)", "180")]
	public async Task Ctu(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqrt(4)", "2")]
	[Arguments("sqrt(-1)", "#-1 IMAGINARY NUMBER")]
	public async Task Sqrt(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("root(4,2)", "2")]
	[Arguments("root(-1,2)", "#-1 IMAGINARY NUMBER")]
	[Arguments("root(27, 3)", "3")]
	[Arguments("root(-27, 3)", "-3")]
	[Arguments("root(125, 5)", "2.62652780440377")]
	public async Task Root(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("round(pi(), 0)", "3")]
	[Arguments("round(pi(), 1)", "3.1")]
	[Arguments("round(pi(), 2)", "3.14")]
	[Arguments("round(pi(), 3)", "3.142")]
	[Arguments("round(pi(), 4)", "3.1416")]
	[Arguments("round(pi(), 5)", "3.14159")]
	[Arguments("round(-[pi()], 3)", "-3.142")]
	[Arguments("round(3.5, 3, 1)", "3.500")]
	[Arguments("round(1.2345, 2, 1)", "1.23")]
	public async Task Round(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("div(13,4)", "3")]
	[Arguments("div(-13,4)", "-3")]
	[Arguments("div(13,-4)", "-3")]
	[Arguments("div(-13,-4)", "3")]
	public async Task Div(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("floordiv(13,4)", "3")]
	[Arguments("floordiv(-13,4)", "-4")]
	[Arguments("floordiv(13,-4)", "-4")]
	[Arguments("floordiv(-13,-4)", "3")]
	public async Task FloorDiv(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("modulo(13,4)", "1")]
	[Arguments("modulo(-13,4)", "3")]
	[Arguments("modulo(13,-4)", "-3")]
	[Arguments("modulo(-13,-4)", "-1")]
	public async Task Modulo(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("remainder(13,4)", "1")]
	[Arguments("remainder(-13,4)", "-1")]
	[Arguments("remainder(13,-4)", "1")]
	[Arguments("remainder(-13,-4)", "-1")]
	public async Task Remainder(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sign(-4)", "-1")]
	[Arguments("sign(4)", "1")]
	[Arguments("sign(0)", "0")]
	public async Task Sign(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mean(1,2,3,4,5)", "3")]
	public async Task Mean(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("median(1,2,3,4,5)", "3")]
	[Arguments("median(1,2,3,4)", "2.5")]
	public async Task Median(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stddev(1,2,3,4,5)", "1.58113883008419")]
	public async Task Stddev(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("log(0)", "-inf")]
	[Arguments("log(1)", "0")]
	[Arguments("log(100)", "2")]
	[Arguments("log(8,2)", "3")]
	[Arguments("log(10,e)", "2.30258509299405")]
	[Arguments("log(9,3)", "2")]
	[Arguments("log(9,foo)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments("log(-5)", "#-1 OUT OF RANGE")]
	public async Task Log(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ln(10)", "2.30258509299405")]
	public async Task Ln(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("fraction(.75)", "3/4")]
	[Arguments("fraction(pi())", "1146408/364913")]
	[Arguments("fraction(2)", "2")]
	[Arguments("fraction(2.75)", "11/4")]
	[Arguments("fraction(2.75, 1)", "2 3/4")]
	[Arguments("fraction(2, 1)", "2")]
	public async Task Fraction(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("inc(0)", "1")]
	[Arguments("inc(-2)", "-1")]
	[Arguments("inc(foo1)", "foo2")]
	[Arguments("inc(1.2)", "1.3")]
	[Arguments("inc(foo)", "#-1 ARGUMENT MUST END IN AN INTEGER")]
	public async Task Inc(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("dec(0)", "-1")]
	[Arguments("dec(-2)", "-3")]
	[Arguments("dec(foo1)", "foo0")]
	[Arguments("dec(1.2)", "1.1")]
	[Arguments("dec(foo)", "#-1 ARGUMENT MUST END IN AN INTEGER")]
	public async Task Dec(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("baseconv(10,10,36)", "a")]
	[Arguments("baseconv(-10,10,36)", "-a")]
	[Arguments("baseconv(9,36,10)", "9")]
	[Arguments("baseconv(-9,36,10)", "-9")]
	[Arguments("baseconv(abc,36,10)", "13368")]
	[Arguments("baseconv(-abc,36,10)", "-13368")]
	[Arguments("baseconv(13368,10,36)", "abc")]
	[Arguments("baseconv(-13368,10,36)", "-abc")]
	[Arguments("baseconv(100,10,64)", "Bk")]
	[Arguments("baseconv(Bk,64,10)", "100")]
	[Arguments("baseconv(-Bk,64,10)", "254052")]
	[Arguments("baseconv(-_,64,10)", "4031")]
	[Arguments("baseconv(+/,64,10)", "4031")]
	[Arguments("baseconv(4031,10,64)", "-_")]
	public async Task BaseConv(string expr, string expected)
	{
		var result = await Parser.FunctionParse(MModule.single(expr));
		await Assert.That(result!.Message!.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// div(), floordiv(), modulo() and remainder() operate on 64-bit signed integers,
	/// matching PennMUSH's IVAL. Expected values were read off a live PennMUSH 1.8.8 oracle.
	/// </summary>
	[Test]
	[Arguments("div(9223372036854775807,1)", "9223372036854775807")]
	[Arguments("div(100,7,2)", "7")]
	[Arguments("floordiv(9223372036854775807,1)", "9223372036854775807")]
	[Arguments("modulo(9223372036854775807,10)", "7")]
	[Arguments("remainder(9223372036854775807,10)", "7")]
	[Arguments("remainder(7,-2)", "1")]
	[Arguments("remainder(-7,-2)", "-1")]
	public async Task IntegerMathIs64Bit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("div(1,0)", "#-1 DIVISION BY ZERO")]
	[Arguments("floordiv(1,0)", "#-1 DIVISION BY ZERO")]
	[Arguments("modulo(1,0)", "#-1 DIVISION BY ZERO")]
	[Arguments("remainder(1,0)", "#-1 DIVISION BY ZERO")]
	[Arguments("div(7.5,2)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments("floordiv(7.5,2)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments("modulo(7.5,2)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments("remainder(7.5,2)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	public async Task IntegerMathRejectsBadArguments(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// Dividing long.MinValue by -1 overflows. PennMUSH returns #-1 DOMAIN ERROR from div(),
	/// but its floordiv() guards against INT_MIN rather than INT64_MIN and dies with SIGFPE;
	/// SharpMUSH returns the domain error from both.
	/// </summary>
	[Test]
	[Arguments("div(-9223372036854775808,-1)", "#-1 DOMAIN ERROR")]
	[Arguments("floordiv(-9223372036854775808,-1)", "#-1 DOMAIN ERROR")]
	public async Task IntegerDivisionOverflowIsADomainError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// A deliberate divergence from PennMUSH, which parses these with a 32-bit parse_integer():
	/// inc(2147483647) wraps to -2147483648 there and trunc() clamps to the int32 range.
	/// </summary>
	[Test]
	[Arguments("inc(2147483647)", "2147483648")]
	[Arguments("dec(-2147483648)", "-2147483649")]
	[Arguments("trunc(9223372036854775807)", "9223372036854775807")]
	[Arguments("trunc(-9999999999)", "-9999999999")]
	public async Task IncDecTruncAre64Bit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// Dividing by zero used to throw out of the decimal fold rather than return an error.
	/// </summary>
	[Test]
	[Arguments("fdiv(1,0)", "#-1 DIVISION BY ZERO")]
	[Arguments("fmod(1,0)", "#-1 DIVISION BY ZERO")]
	public async Task FloatingDivisionByZeroIsAnError(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	/// <summary>
	/// The whole part of a fraction is 64-bit. PennMUSH prints it through a 32-bit conversion,
	/// so its fraction(3000000000.5,1) overflows to -2147483648.
	/// </summary>
	[Test]
	[Arguments("fraction(3000000000.5)", "6000000001/2")]
	[Arguments("fraction(3000000000.5,1)", "3000000000 1/2")]
	public async Task FractionWholePartIs64Bit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lmath(band,-1 1)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	[Arguments("lmath(div,7.5 2)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments("lmath(div,1 0)", "#-1 DIVISION BY ZERO")]
	public async Task LMathIntegerOperationsRejectBadArguments(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lmath(div,9223372036854775807 1)", "9223372036854775807")]
	[Arguments("lmath(modulo,9223372036854775807 10)", "7")]
	[Arguments("lmath(remainder,-7 2)", "-1")]
	[Arguments("lmath(band,4294967296 4294967296)", "4294967296")]
	[Arguments("lmath(bor,4294967296 1)", "4294967297")]
	[Arguments("lmath(bxor,4294967296 1)", "4294967297")]
	public async Task LMathIntegerOperationsAre64Bit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
