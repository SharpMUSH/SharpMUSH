using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class InformationFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("type(%#)", "PLAYER")]
	[Arguments("type(%l)", "ROOM")]
	public async Task Type(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task MudName()
	{
		var result = (await Parser.FunctionParse(MModule.single("mudname()")))?.Message!;
		// Should return a non-empty string
		await Assert.That(result.ToPlainText()).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test, Skip("Not Yet Implemented")]
	public async Task Name()
	{
		var result = (await Parser.FunctionParse(MModule.single("name(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("One");
	}
}
