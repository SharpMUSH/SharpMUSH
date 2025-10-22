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

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("doing(%#)", "")]
	public async Task Doing(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("host(%#)", "")]
	public async Task Host(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("ipaddr(%#)", "")]
	public async Task Ipaddr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("lports()", "")]
	public async Task Lports(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("mwho()", "")]
	public async Task Mwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nwho()", "1")]
	public async Task Nwho(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
