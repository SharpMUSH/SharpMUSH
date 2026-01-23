using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class VectorFunctionUnitTests : TestClassFactory
{
	private IMUSHCodeParser Parser => FunctionParser;

	[Test]
	[Arguments("vcross(1 0 0,0 1 0)", "0 0 1")]
	[Arguments("vcross(4 5 6,7 8 9)", "-3 6 -3")]
	[Arguments("vcross(1 2 3,4 5 6)", "-3 6 -3")]
	[Arguments("vcross(0 0 1,1 0 0)", "0 1 0")]
	public async Task Vcross(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("vmag(3 4)", "5")]
	[Arguments("vmag(0 0)", "0")]
	[Arguments("vmag(1 0)", "1")]
	[Arguments("vmag(3 4 0)", "5")]
	[Arguments("vmag(1 1 1)", "1.7320508075688772")]
	public async Task Vmag(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("vunit(3 4)", "")]
	public async Task Vunit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
