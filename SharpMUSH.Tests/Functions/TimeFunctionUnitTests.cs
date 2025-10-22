using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class TimeFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Secs()
	{
		var result = (await Parser.FunctionParse(MModule.single("secs()")))?.Message!;
		// Result should be a positive number (Unix timestamp)
		await Assert.That(long.Parse(result.ToPlainText())).IsGreaterThan(0);
	}

	[Test]
	public async Task Time()
	{
		var result = (await Parser.FunctionParse(MModule.single("time()")))?.Message!;
		// Result should be in the format "Day Mon DD HH:MM:SS YYYY"
		// Just check it's not empty and has the right structure
		var parts = result.ToPlainText().Split(' ');
		await Assert.That(parts.Length).IsGreaterThanOrEqualTo(5);
	}

	[Test]
	public async Task Uptime()
	{
		var result = (await Parser.FunctionParse(MModule.single("uptime()")))?.Message!;
		// Uptime should be a positive number (as a long to handle large values)
		await Assert.That(long.Parse(result.ToPlainText())).IsGreaterThanOrEqualTo(0);
	}
}
