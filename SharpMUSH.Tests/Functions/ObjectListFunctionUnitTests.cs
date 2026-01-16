using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ObjectListFunctionUnitTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.FunctionParser;

	[Test]
	[Arguments("lvcon(#0)", "")]
	public async Task Lvcon(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lvexits(#0)", "")]
	public async Task Lvexits(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lvplayers(#0)", "")]
	public async Task Lvplayers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lvthings(#0)", "")]
	public async Task Lvthings(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("nlsearch(all,type,player)", "")]
	public async Task Nlsearch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
