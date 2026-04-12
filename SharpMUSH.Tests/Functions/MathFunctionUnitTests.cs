using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class MathFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("add(1,2)", "3")]
	[Arguments("add(1.5,5)", "6.5")]
	[Arguments("add(-1.5,5)", "3.5")]
	[Arguments("add(1,1,1,1)", "4")]
	public async Task Add(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sub(1,2)", "-1")]
	[Arguments("sub(5,1.5)", "3.5")]
	[Arguments("sub(-1.5,5)", "-6.5")]
	[Arguments("sub(1,1,1,1)", "-2")]
	public async Task Sub(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lnum(0)", "")]
	[Arguments("lnum(1)", "0")]
	[Arguments("lnum(5)", "0 1 2 3 4")]
	[Arguments("lnum(0,0)", "0")]
	[Arguments("lnum(0,1)", "0 1")]
	[Arguments("lnum(0,5)", "0 1 2 3 4 5")]
	[Arguments("lnum(0,5,|)", "0|1|2|3|4|5")]
	[Arguments("lnum(0,5,|,2)", "0|2|4")]
	[Arguments("lnum(0,5,,5)", "05")]
	[Arguments("lnum(0,5,-,5)", "0-5")]
	// Penn lnum.2-lnum.14
	[Arguments("lnum(#1)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("lnum(foo)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments("lnum(4.5)", "0 1 2 3")]
	[Arguments("lnum(1,5)", "1 2 3 4 5")]
	[Arguments("lnum(1,4,@)", "1@2@3@4")]
	[Arguments("lnum(1,5,@,2)", "1@3@5")]
	[Arguments("lnum(1,5,,2)", "135")]
	[Arguments("lnum(-2,2)", "-2 -1 0 1 2")]
	public async Task LNum(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mul(2,3)", "6")]
	[Arguments("mul(2.5,4)", "10")]
	[Arguments("mul(2.3,4)", "9.2")]
	[Arguments("mul(-2,3)", "-6")]
	[Arguments("mul(2,3,4)", "24")]
	public async Task Mul(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("div(6,2)", "3")]
	[Arguments("div(10,4)", "2")]
	[Arguments("div(-6,2)", "-3")]
	[Arguments("div(24,2,2)", "6")]
	[Arguments("div(10,0)", "#-1 DIVIDE BY ZERO")]
	// Penn div.1-div.4
	[Arguments("div(13,4)", "3")]
	[Arguments("div(-13,4)", "-3")]
	[Arguments("div(13,-4)", "-3")]
	[Arguments("div(-13,-4)", "3")]
	public async Task Div(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("modulo(7,3)", "1")]
	[Arguments("modulo(10,3)", "1")]
	[Arguments("modulo(-7,3)", "2")]
	[Arguments("mod(-7,3)", "2")] // Alias Test
	[Arguments("modulo(10,0)", "#-1 DIVIDE BY ZERO")]
	// Penn mod.1-mod.4
	[Arguments("modulo(13,4)", "1")]
	[Arguments("modulo(-13,4)", "3")]
	[Arguments("modulo(13,-4)", "-3")]
	[Arguments("modulo(-13,-4)", "-1")]
	public async Task Mod(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("eq(1,2)", "0")]
	[Arguments("eq(1,1)", "1")]
	[Arguments("eq(wood,1)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	public async Task Eq(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("gt(1,2)", "0")]
	[Arguments("gt(1,1)", "0")]
	[Arguments("gt(1,0)", "1")]
	[Arguments("gt(1,0,0)", "0")]
	[Arguments("gt(2,1,0)", "1")]
	[Arguments("gt(2,1,2)", "0")]
	[Arguments("gt(wood,1)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	public async Task Gt(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lt(1,2)", "1")]
	[Arguments("lt(1,1)", "0")]
	[Arguments("lt(1,0)", "0")]
	[Arguments("lt(0,1,2)", "1")]
	[Arguments("lt(2,1,0)", "0")]
	[Arguments("lt(2,1,2)", "0")]
	[Arguments("lt(wood,1)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	public async Task Lt(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lte(1,2)", "1")]
	[Arguments("lte(1,1)", "1")]
	[Arguments("lte(1,0)", "0")]
	[Arguments("lte(1,0,0)", "0")]
	[Arguments("lte(-1,0,0)", "1")]
	[Arguments("lte(2,1,0)", "0")]
	[Arguments("lte(1,2,3)", "1")]
	[Arguments("lte(2,1,2)", "0")]
	[Arguments("lte(wood,1)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	public async Task Lte(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("round(3.14159,2)", "3.14")]
	[Arguments("round(3.5,3,1)", "3.500")]
	// Penn round.0-round.8
	[Arguments("round(pi(),0)", "3")]
	[Arguments("round(pi(),1)", "3.1")]
	[Arguments("round(pi(),2)", "3.14")]
	[Arguments("round(pi(),3)", "3.142")]
	[Arguments("round(pi(),4)", "3.1416")]
	[Arguments("round(pi(),5)", "3.14159")]
	[Arguments("round(1.2345,2,1)", "1.23")]
	public async Task Round(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("abs(5)", "5")]
	[Arguments("abs(-5)", "5")]
	[Arguments("abs(-3.14)", "3.14")]
	// Penn abs.1-abs.7
	[Arguments("abs(-1)", "1")]
	[Arguments("abs(-1.5)", "1.5")]
	[Arguments("abs(1)", "1")]
	[Arguments("abs(0)", "0")]
	[Arguments("abs(-0)", "0")]
	[Arguments("abs(99999999999)", "99999999999")]
	[Arguments("abs(-99999999999)", "99999999999")]
	public async Task Abs(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("gte(2,1)", "1")]
	[Arguments("gte(1,1)", "1")]
	[Arguments("gte(0,1)", "0")]
	[Arguments("gte(2,1,1)", "1")]
	[Arguments("gte(1,2,1)", "0")]
	[Arguments("gte(wood,1)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	public async Task Gte(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("max(1,2,3)", "3")]
	[Arguments("max(-1,-2,-3)", "-1")]
	[Arguments("max(1.5,2.5,3.5)", "3.5")]
	public async Task Max(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("min(1,2,3)", "1")]
	[Arguments("min(-1,-2,-3)", "-3")]
	[Arguments("min(1.5,2.5,3.5)", "1.5")]
	public async Task Min(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("floor(3.14)", "3")]
	[Arguments("floor(3.0)", "3")]
	[Arguments("floor(-3.14)", "-4")]
	[Arguments("floor(3.14159)", "3")]
	public async Task Floor(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ceil(3.14159)", "4")]
	[Arguments("ceil(3.0)", "3")]
	[Arguments("ceil(-3.14)", "-3")]
	public async Task Ceil(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("trunc(3.14)", "3")]
	[Arguments("trunc(3.99)", "3")]
	[Arguments("trunc(-3.14)", "-3")]
	public async Task Trunc(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sign(5)", "1")]
	[Arguments("sign(-5)", "-1")]
	[Arguments("sign(0)", "0")]
	public async Task Sign(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sqrt(4)", "2")]
	[Arguments("sqrt(9)", "3")]
	[Arguments("sqrt(2)", "1.4142135623730951")]
	// Penn sqrt.2
	[Arguments("sqrt(-1)", "#-1 IMAGINARY NUMBER")]
	public async Task Sqrt(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("fdiv(7,2)", "3.5")]
	[Arguments("fdiv(10,4)", "2.5")]
	public async Task Fdiv(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("floordiv(7,2)", "3")]
	[Arguments("floordiv(-7,2)", "-4")]
	// Penn floordiv.1-floordiv.4
	[Arguments("floordiv(13,4)", "3")]
	[Arguments("floordiv(-13,4)", "-4")]
	[Arguments("floordiv(13,-4)", "-4")]
	[Arguments("floordiv(-13,-4)", "3")]
	public async Task Floordiv(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("inc(5)", "6")]
	[Arguments("inc(-1)", "0")]
	// Penn inc.1-inc.5
	[Arguments("inc(0)", "1")]
	[Arguments("inc(-2)", "-1")]
	[Arguments("inc(foo1)", "foo2")]
	[Arguments("inc(1.2)", "1.3")]
	[Arguments("inc(foo)", "#-1 ARGUMENT MUST END IN AN INTEGER")]
	public async Task Inc(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mean(1,2,3)", "2")]
	[Arguments("mean(10,20,30)", "20")]
	// Penn mean.1
	[Arguments("mean(1,2,3,4,5)", "3")]
	public async Task Mean(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("median(1,2,3)", "2")]
	[Arguments("median(1,2,3,4)", "2.5")]
	// Penn median.1-median.2
	[Arguments("median(1,2,3,4,5)", "3")]
	public async Task Median(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("bound(5,1,10)", "5")]
	[Arguments("bound(0,1,10)", "1")]
	[Arguments("bound(15,1,10)", "10")]
	public async Task Bound(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("lmath(add,1|2|3,|)", "6")]
	[Arguments("lmath(max,1 2 3)", "3")]
	public async Task Lmath(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sin(0)", "0")]
	[Arguments("cos(0)", "1")]
	public async Task TrigFunctions(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("log(10)", "1")]
	[Arguments("log(100)", "2")]
	// Penn log.1-log.9
	[Arguments("log(0)", "-Infinity")]
	[Arguments("log(1)", "0")]
	[Arguments("log(8,2)", "3")]
	[Arguments("log(9,3)", "2")]
	[Arguments("log(9,foo)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments("log(-5)", "NaN")]
	public async Task Log(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("dist2d(0,0,3,4)", "5")]
	public async Task Dist2d(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("dist3d(0,0,0,1,1,1)", "1.7320508075688772")]
	public async Task Dist3d(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("acos(1)", "0")]
	[Arguments("asin(0)", "0")]
	[Arguments("atan(0)", "0")]
	public async Task InverseTrigFunctions(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("tan(0)", "0")]
	// Penn tan.1-tan.4
	[Arguments("tan(45,d)", "1")]
	public async Task Tan(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("fmod(7,3)", "1")]
	public async Task Fmod(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stddev(1,2,3,4,5)", "1.5811388300841898")]
	public async Task Stddev(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("root(8,3)", "2")]
	[Arguments("root(16,2)", "4")]
	// Penn root.1-root.4
	[Arguments("root(4,2)", "2")]
	[Arguments("root(-1,2)", "#-1 IMAGINARY NUMBER")]
	[Arguments("root(27,3)", "3")]
	[Arguments("root(-27,3)", "-3")]
	public async Task Root(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("fraction(0.75)", "3/4")]
	[Arguments("fraction(2)", "2")]
	[Arguments("fraction(2.75)", "11/4")]
	[Arguments("fraction(2.75,1)", "2 3/4")]
	// Penn fraction.6
	[Arguments("fraction(2,1)", "2")]
	public async Task Fraction(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("acos(0)", "1.5707963267948966")]
	[Arguments("acos(1)", "0")]
	public async Task Acos(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("asin(0)", "0")]
	[Arguments("asin(1)", "1.5707963267948966")]
	public async Task Asin(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("atan(0)", "0")]
	[Arguments("atan(1)", "0.7853981633974483")]
	public async Task Atan(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cos(0)", "1")]
	[Arguments("cos(90,d)", "0")]
	[Arguments("cos(1.570796)", "0")]
	// Penn cos.2-cos.3
	[Arguments("cos(pi(),r)", "-1")]
	[Arguments("cos(pi())", "-1")]
	public async Task Cos(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sin(0)", "0")]
	[Arguments("sin(1.5707963267948966)", "1")]
	// Penn sin.1
	[Arguments("sin(90,d)", "1")]
	public async Task Sin(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ln(1)", "0")]
	[Arguments("ln(2.718281828459045)", "1")]
	public async Task Ln(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ctu(90,d,r)", "1.5707963267948966")]  // 90 degrees to radians
	[Arguments("ctu(0,d,r)", "0")]  // 0 degrees to radians
	[Arguments("ctu(180,d,r)", "3.141592653589793")]  // 180 degrees to radians
	// Penn ctu.2
	[Arguments("ctu(pi(),r,d)", "180")]  // pi radians to degrees
	public async Task CTU(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("dec(1)", "0")]
	[Arguments("dec(5)", "4")]
	[Arguments("dec(-1)", "-2")]
	[Arguments("dec(0)", "-1")]
	[Arguments("dec(100)", "99")]
	[Arguments("dec(3.5)", "3.4")]
	// Penn dec.1-dec.5
	[Arguments("dec(-2)", "-3")]
	[Arguments("dec(foo1)", "foo0")]
	[Arguments("dec(1.2)", "1.1")]
	[Arguments("dec(foo)", "#-1 ARGUMENT MUST END IN AN INTEGER")]
	public async Task Dec(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn remainder.1-remainder.4
	[Test]
	[Arguments("remainder(13,4)", "1")]
	[Arguments("remainder(-13,4)", "-1")]
	[Arguments("remainder(13,-4)", "1")]
	[Arguments("remainder(-13,-4)", "-1")]
	public async Task Remainder(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn atan2.1-atan2.4
	[Test]
	[Arguments("atan2(0,-1)", "3.141592653589793")]
	[Arguments("atan2(0,1)", "0")]
	[Arguments("atan2(-0.0001,0)", "-1.5707963267948966")]
	[Arguments("atan2(0.0001,0)", "1.5707963267948966")]
	public async Task Atan2(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn acos with nested calls
	[Test]
	[Arguments("acos(cos(90,d),d)", "90")]
	[Arguments("acos(cos(1,r))", "1")]
	[Arguments("acos(cos(1,r),r)", "1")]
	public async Task AcosNested(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn asin with nested calls
	[Test]
	[Arguments("asin(sin(90,d),d)", "90")]
	[Arguments("asin(sin(1,r))", "1")]
	[Arguments("asin(sin(1,r),r)", "1")]
	public async Task AsinNested(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn atan with nested calls
	[Test]
	[Arguments("atan(tan(45,d),d)", "45")]
	[Arguments("atan(tan(1,r))", "1")]
	[Arguments("atan(tan(1,r),r)", "1")]
	public async Task AtanNested(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	// Penn ln.1 — ln(10) = natural log of 10
	[Test]
	[Arguments("ln(10)", "2.302585092994046")]
	public async Task LnPenn(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
}