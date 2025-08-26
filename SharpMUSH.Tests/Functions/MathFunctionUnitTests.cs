using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
	
	[Test]
	[Arguments("eq(1,2)", "0")]
	[Arguments("eq(1,1)", "1")]
	[Arguments("eq(wood,1)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	public async Task Eq(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	/*
		Not yet Implemented.

		[Test]
		[Arguments("round(3.14, 1)", "3")]
		[Arguments("round(3.5,1)", "4")]
		[Arguments("round(-3.5,1)", "-4")]
		[Arguments("round(3.14159, 2)", "3.14")]
		public async Task Round(string str, string expected)
		{
				Console.WriteLine("Testing: {0}", str);

				var result = (await _parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

				await Assert.That(result).IsEqualTo(expected);
		}
	*/

	[Test]
	[Arguments("abs(5)", "5")]
	[Arguments("abs(-5)", "5")]
	[Arguments("abs(-3.14)", "3.14")]
	public async Task Abs(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
}