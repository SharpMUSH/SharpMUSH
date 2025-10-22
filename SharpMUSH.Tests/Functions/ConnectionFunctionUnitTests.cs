using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ConnectionFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Skip("Is empty. Needs investigation.")]
	public async Task Idle()
	{
		// Test idle function - should return idle time in seconds
		var result = (await Parser.FunctionParse(MModule.single("idle(%#)")))?.Message!;
		// Result should be a valid string (could be empty for some implementations)
		await Assert.That(result.ToPlainText()).HasLength().Positive;
	}

	[Test]
	public async Task Conn()
	{
		// Test conn function (uppercase) - should return connection number or info
		var result = (await Parser.FunctionParse(MModule.single("conn(%#)")))?.Message!;
		// Result should be a valid string
		await Assert.That(result.ToPlainText()).HasLength().Positive;
	}

	[Test]
	public async Task ListWho()
	{
		// Test lwho function - should return a list of connected players
		var result = (await Parser.FunctionParse(MModule.single("lwho()")))?.Message!;
		// The result should be a valid string
		await Assert.That(result.ToPlainText()).HasLength().Positive;
	}
}
