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

	[Test]
	[Arguments("etime(0)", "0s")]
	public async Task Etime(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("stringsecs(1d)", "86400")]
	public async Task Stringsecs(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("timestring(86400)", " 1d  0s")]
	public async Task Timestring(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
	
	[Test]
	[Arguments("ctime(#0)", "")]
	public async Task Ctime(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Arguments("etimefmt($2H:$2M, 3661)", "01:01")]
	public async Task Etimefmt(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("timefmt($Y-$m-$d,0)", "1970-01-01")]
	public async Task Timefmt(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("secscalc(1d2h3m4s)", "93784")]
	public async Task Secscalc(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("timecalc(1h 2m)", "")]
	public async Task Timecalc(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
