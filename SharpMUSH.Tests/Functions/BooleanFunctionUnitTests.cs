using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class BooleanFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("t(1)", "1")]
	[Arguments("t(0)", "0")]
	[Arguments("t(true)", "1")]
	[Arguments("t(false)", "1")]
	[Arguments("t(#-1 Words)", "0")]
	[Arguments("t()", "0")]
	[Arguments("t( )", "0")]
	[Arguments("t(%b)", "1")]
	public async Task T(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("and(1,1)", "1")]
	[Arguments("and(0,1)", "0")]
	[Arguments("and(0,0,1)", "0")]
	[Arguments("and(1,1,1)", "1")]
	public async Task And(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("nand(1,1)", "0")]
	[Arguments("nand(0,1)", "1")]
	[Arguments("nand(0,0,1)", "1")]
	[Arguments("nand(1,1,1)", "0")]
	public async Task Nand(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("or(1,1)", "1")]
	[Arguments("or(0,1)", "1")]
	[Arguments("or(0,0)", "0")]
	[Arguments("or(0,0,1)", "1")]
	[Arguments("or(1,1,1)", "1")]
	public async Task Or(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("nor(1,1)", "0")]
	[Arguments("nor(0,1)", "0")]
	[Arguments("nor(0,0)", "1")]
	[Arguments("nor(0,0,1)", "0")]
	[Arguments("nor(1,1,1)", "0")]
	public async Task Nor(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("xor(1,1)", "0")]
	[Arguments("xor(0,1)", "1")]
	[Arguments("xor(1,0)", "1")]
	[Arguments("xor(0,0)", "0")]
	public async Task Xor(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("not(1)", "0")]
	[Arguments("not(0)", "1")]
	[Arguments("not(true)", "0")]
	[Arguments("not(false)", "0")]
	public async Task Not(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cand(1,1)", "1")]
	[Arguments("cand(0,1)", "0")]
	[Arguments("cand(0,0,1)", "0")]
	[Arguments("cand(1,1,1)", "1")]
	public async Task Cand(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cor(1,1)", "1")]
	[Arguments("cor(0,1)", "1")]
	[Arguments("cor(0,0)", "0")]
	[Arguments("cor(0,0,1)", "1")]
	public async Task Cor(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
}
