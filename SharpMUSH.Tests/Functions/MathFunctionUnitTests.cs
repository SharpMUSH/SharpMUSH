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
}
