using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class InformationFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("type(%#)", "PLAYER")]
	public async Task Type(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task Mudname()
	{
		var result = (await Parser.FunctionParse(MModule.single("mudname()")))?.Message!;
		// Should return a non-empty string
		await Assert.That(result.ToPlainText()).IsNotEmpty();
	}

	[Test]
	public async Task Name()
	{
		var result = (await Parser.FunctionParse(MModule.single("name(%#)")))?.Message!;
		// Should return a non-empty name for the current player
		await Assert.That(result.ToPlainText()).IsNotEmpty();
	}
}
