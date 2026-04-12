using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ConditionalFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("condall(1 1 1,YES,NO)", "YES")]
	[Arguments("condall(1 0 1,YES,NO)", "NO")]
	public async Task Condall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ncond(1,a,0,b,c)", "a")]
	[Arguments("ncond(0,a,1,b,c)", "b")]
	public async Task Ncond(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ncondall(1 1,a,0 1,b,c)", "a")]
	public async Task Ncondall(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn firstof.1-firstof.4
	[Test]
	[Arguments("firstof(0,0,2)", "2")]
	[Arguments("firstof(2,0,0)", "2")]
	[Arguments("firstof(0,0,0)", "0")]
	[Arguments("firstof(1,2,3)", "1")]
	public async Task Firstof(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn allof.1-allof.9
	[Test]
	[Arguments("allof(0,0,2,)", "2")]
	[Arguments("allof(2,0,0,)", "2")]
	[Arguments("allof(0,0,0,)", "")]
	[Arguments("allof(1,2,3,%b)", "1 2 3")]
	[Arguments("allof(1,2,3,)", "123")]
	[Arguments("allof(0,0,2,@)", "2")]
	[Arguments("allof(2,0,0,@)", "2")]
	[Arguments("allof(0,0,0,@)", "")]
	[Arguments("allof(1,2,3,@)", "1@2@3")]
	public async Task Allof(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn strfirstof.1-strfirstof.4
	[Test]
	[Arguments("strfirstof(,,foo)", "foo")]
	[Arguments("strfirstof(,bar,foo)", "bar")]
	[Arguments("strfirstof(bar,,foo)", "bar")]
	[Arguments("strfirstof(bar,baz,foo)", "bar")]
	public async Task Strfirstof(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// Penn strallof.1-strallof.8
	[Test]
	[Arguments("strallof(,,,@)", "")]
	[Arguments("strallof(foo,@)", "foo")]
	[Arguments("strallof(,foo,@)", "foo")]
	[Arguments("strallof(foo,,@)", "foo")]
	[Arguments("strallof(foo,bar,@)", "foo@bar")]
	[Arguments("strallof(,foo,bar,@)", "foo@bar")]
	[Arguments("strallof(foo,,bar,@)", "foo@bar")]
	[Arguments("strallof(foo,bar,,@)", "foo@bar")]
	public async Task Strallof(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
