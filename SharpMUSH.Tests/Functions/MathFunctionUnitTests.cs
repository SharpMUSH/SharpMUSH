using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class MathFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

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
	public async Task LNum(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mul(2,3)", "6")]
	[Arguments("mul(2.5,4)", "10.0")] // TODO: This should return 10
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
	public async Task Div(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("modulo(7,3)", "1")]
	[Arguments("modulo(10,3)", "1")]
	[Arguments("modulo(-7,3)", "-1")]
	[Arguments("mod(-7,3)", "-1")] // Alias Test
	[Arguments("modulo(10,0)", "#-1 DIVIDE BY ZERO")]
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
	public async Task Floordiv(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("inc(5)", "6")]
	[Arguments("inc(-1)", "0")]
	public async Task Inc(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("mean(1,2,3)", "2")]
	[Arguments("mean(10,20,30)", "20")]
	public async Task Mean(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("median(1,2,3)", "2")]
	[Arguments("median(1,2,3,4)", "2.5")]
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
	[Arguments("stddev(1,2,3,4,5)", "1.4142135623730951")]
	public async Task Stddev(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("root(8,3)", "2")]
	[Arguments("root(16,2)", "4")]
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
	[Arguments("cos(90,d)", "6.123233995736766E-17")]
	[Arguments("cos(1.570796)", "3.2679489653813835E-07")]
	public async Task Cos(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("sin(0)", "0")]
	[Arguments("sin(1.5707963267948966)", "1")]
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
}