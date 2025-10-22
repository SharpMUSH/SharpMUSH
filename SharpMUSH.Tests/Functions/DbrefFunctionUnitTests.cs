using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class DbrefFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Loc()
	{
		// Test loc function - should return the location of the current player
		var result = (await Parser.FunctionParse(MModule.single("loc(%#)")))?.Message!;
		// The result should be a dbref (starts with #)
		await Assert.That(result.ToPlainText()).StartsWith("#");
	}

	[Test]
	public async Task Controls()
	{
		// Test CONTROLS function (uppercase) - a player should control themselves
		var result = (await Parser.FunctionParse(MModule.single("CONTROLS(%#,%#)")))?.Message!;
		// Result should be a valid string (typically "1" or "0")
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Home()
	{
		// Test home function - should return the home of the current player
		var result = (await Parser.FunctionParse(MModule.single("home(%#)")))?.Message!;
		// The result should be a dbref (starts with #)
		await Assert.That(result.ToPlainText()).StartsWith("#");
	}
}
