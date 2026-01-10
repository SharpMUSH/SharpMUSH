using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ObjectFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("create(TestObject)", "")]
	public async Task Create(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("dig(TestRoom)", "")]
	public async Task Dig(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("clone(%#)", "")]
	public async Task Clone(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("link(%#,#0)", "")]
	public async Task Link(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("open(TestExit,#0)", "")]
	public async Task Open(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("tel(%#,#0)", "")]
	public async Task Tel(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("wipe(%#/testattr)", "")]
	public async Task Wipe(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("lsearch(all,type,PLAYER)", "")]
	public async Task Lsearch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Failing")]
	[Arguments("nlsearch(all,type,PLAYER)", "1")]
	public async Task Nlsearch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(int.Parse(result.ToPlainText())).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	[Skip("Failing")]
	[Arguments("nsearch(all,type,PLAYER)", "1")]
	public async Task Nsearch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(int.Parse(result.ToPlainText())).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	[Arguments("lsearchr(all,type,PLAYER)", "")]
	public async Task Lsearchr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("textsearch(all,One)", "")]
	public async Task Textsearch(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("next(%#)", "")]
	public async Task Next(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
