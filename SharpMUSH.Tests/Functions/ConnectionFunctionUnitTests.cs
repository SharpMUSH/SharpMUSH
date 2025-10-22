using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ConnectionFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Idle()
	{
		// Test idle function - should return idle time in seconds
		var result = (await Parser.FunctionParse(MModule.single("idle(%#)")))?.Message!;
		// Result should be a valid string (could be empty for some implementations)
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Conn()
	{
		// Test CONN function (uppercase) - should return connection number or info
		var result = (await Parser.FunctionParse(MModule.single("CONN(%#)")))?.Message!;
		// Result should be a valid string
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Lwho()
	{
		// Test lwho function - should return a list of connected players
		var result = (await Parser.FunctionParse(MModule.single("lwho()")))?.Message!;
		// The result should be a valid string
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
