using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class BitwiseFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("10", "10", "2", "1010")]
	[Arguments("10", "2", "10", "2")]
	[Arguments("woof", "64", "32", "c52gv")]
	[Arguments("oof", "32", "64", "GMP")]
	[Arguments("woof", "1", "10", "#-1 Argument 1 must be between 2 and 64.")]
	[Arguments("woof", "10", "1", "#-1 Argument 2 must be between 2 and 64.")]
	[Arguments("woof", "10", "65", "#-1 Argument 2 must be between 2 and 64.")]
	[Arguments("woof", "65", "10", "#-1 Argument 1 must be between 2 and 64.")]
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
}