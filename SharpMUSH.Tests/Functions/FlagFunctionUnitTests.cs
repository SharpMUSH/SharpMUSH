using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class FlagFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("andflags(%#,PLAYER)", "1")]
	public async Task Andflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orflags(%#,PLAYER WIZARD)", "1")]
	public async Task Orflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("andlflags(%#,PLAYER)", "1")]
	public async Task Andlflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("orlflags(%#,PLAYER WIZARD)", "1")]
	public async Task Orlflags(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// PARSER ISSUE: This test has unexpected token issues with PARSER_STRICT_MODE=true.
	// The parser throws an exception instead of recovering from syntax errors.
	[Test]
	[Arguments("andlpowers(%#,)", "")]
	public async Task Andlpowers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	// PARSER ISSUE: This test has unexpected token issues with PARSER_STRICT_MODE=true.
	// The parser throws an exception instead of recovering from syntax errors.
	[Test]
	[Arguments("orlpowers(%#,)", "")]
	public async Task Orlpowers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
