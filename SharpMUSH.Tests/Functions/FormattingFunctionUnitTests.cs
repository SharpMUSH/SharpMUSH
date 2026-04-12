using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class FormattingFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("render(test %r newline)", "")]
	public async Task Render(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("tag(b,text)", "#-1 USE TAGWRAP INSTEAD")]
	public async Task Tag(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("tagwrap(b,text)", "<b>text</b>")]
	public async Task Tagwrap(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("endtag(b)", "#-1 USE TAGWRAP INSTEAD")]
	public async Task Endtag(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("wrap(test,5)", "")]
	public async Task Wrap(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("ljust(test,10)", "test      ")]
	// Penn ljust.1-ljust.5
	[Arguments("ljust(foo,3)", "foo")]
	[Arguments("ljust(foo,5,=)", "foo==")]
	[Arguments("ljust(foo,2)", "foo")]
	[Arguments("ljust(foo,-3)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	public async Task Ljust(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("rjust(test,10)", "      test")]
	// Penn rjust.1-rjust.5
	[Arguments("rjust(foo,3)", "foo")]
	[Arguments("rjust(foo,5)", "  foo")]
	[Arguments("rjust(foo,5,=)", "==foo")]
	[Arguments("rjust(foo,2)", "foo")]
	[Arguments("rjust(foo,-3)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	public async Task Rjust(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("center(test,10)", "   test   ")]
	// Penn center.1-center.7
	[Arguments("center(foo,3)", "foo")]
	[Arguments("center(foo,5,=)", "=foo=")]
	[Arguments("center(foo,2)", "foo")]
	[Arguments("center(foo,-3)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments("center(foo,5,=,~)", "=foo~")]
	public async Task Center(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("table(a b c,10,2)", "")]
	public async Task Table(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
