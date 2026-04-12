using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class VectorFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

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

	// Penn dist2d.1-dist2d.4
	[Test]
	[Arguments("dist2d(0,0,0,0)", "0")]
	[Arguments("dist2d(0,0,0,1)", "1")]
	public async Task Dist2d(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn dist3d.3-dist3d.4
	[Test]
	[Arguments("dist3d(0,0,0,0,0,0)", "0")]
	[Arguments("dist3d(0,0,0,1,0,0)", "1")]
	public async Task Dist3d(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
