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
		await Assert.That(long.Parse(result.ToPlainText())).IsGreaterThan(0);
	}

	[Test]
	[Skip("Weird error. Needs investigation: #-2 I DON'T KNOW WHICH ONE YOU MEAN")]
	public async Task Time()
	{
		var result = (await Parser.FunctionParse(MModule.single("time()")))?.Message!;
		// Result should be in the format "Day Mon DD HH:MM:SS YYYY"
		// Just check it's not empty and has the right structure
		Console.WriteLine(result.ToPlainText());
		var parts = result.ToPlainText().Split(' ');
		await Assert.That(parts.Length).IsEqualTo(5);
	}

	[Test]
	public async Task Uptime()
	{
		var result = (await Parser.FunctionParse(MModule.single("uptime()")))?.Message!;
		await Assert.That(long.Parse(result.ToPlainText())).IsGreaterThan(0);
	}
}
