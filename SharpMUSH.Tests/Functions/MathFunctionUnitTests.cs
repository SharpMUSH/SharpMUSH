namespace SharpMUSH.Tests.Functions;

public class MathFunctionUnitTests: BaseUnitTest
{
	[Test]
	[Arguments("add(1,2)", "3")]
	[Arguments("add(1.5,5)", "6.5")]
	[Arguments("add(-1.5,5)", "3.5")]
	[Arguments("add(1,1,1,1)", "4")]
	public async Task Add(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

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

		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

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
	[Arguments("lnum(0,5,,5)", "0 5")]
	public async Task LNum(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var parser = TestParser();
		var result = (await parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
}
