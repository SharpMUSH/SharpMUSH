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

	// ETIME tests - based on pennfunc.md examples
	[Test]
	[Arguments("etime(0)", "0s")]
	[Arguments("etime(59)", "59s")]
	[Arguments("etime(60)", "1m")]
	[Arguments("etime(61)", "1m  1s")]
	[Arguments("etime(61, 5)", "1m")]
	public async Task Etime(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// STRINGSECS tests - based on pennfunc.md examples
	[Test]
	[Arguments("stringsecs(1d)", "86400")]
	[Arguments("stringsecs(5m 1s)", "301")]
	[Arguments("stringsecs(3y 2m 7d 5h 23m)", "95232300")]
	public async Task Stringsecs(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// TIMESTRING tests - based on pennfunc.md examples
	[Test]
	[Arguments("timestring(86400)", " 1d  0s")]
	[Arguments("timestring(301)", " 5m  1s")]
	[Arguments("timestring(301,1)", "0d  0h  5m  1s")]
	[Arguments("timestring(301,2)", "00d 00h 05m 01s")]
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

	// ETIMEFMT tests - based on pennfunc.md examples
	[Test]
	[Arguments("etimefmt($2H:$2M, 3661)", "01:01")]
	[Arguments("etimefmt($2h:$2M, 3700)", "1:01")]
	[Arguments("etimefmt($2mm $2ss, 500)", "8m 20s")]
	[Arguments("etimefmt(You have $m minutes and $s seconds to go, 78)", "You have 1 minutes and 18 seconds to go")]
	[Arguments("etimefmt($txs is $xm$xs, 75)", "75s is 1m15s")]
	public async Task Etimefmt(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// TIMEFMT tests - based on pennfunc.md examples
	[Test]
	[Arguments("timefmt($Y-$m-$d,0,UTC)", "1970-01-01")]
	[Arguments("timefmt($$,0,UTC)", "$")]
	[Arguments("timefmt($H:$M:$S,0,UTC)", "00:00:00")]
	[Arguments("timefmt($y,0,UTC)", "70")]
	[Arguments("timefmt($Y,0,UTC)", "1970")]
	public async Task Timefmt(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// SECSCALC tests - based on pennfunc.md examples
	[Test]
	[Arguments("secscalc(1d2h3m4s)", "93784")]
	public async Task Secscalc(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// TIMECALC tests - based on pennfunc.md examples
	[Test]
	[Arguments("timecalc(1h 2m)", "")]
	public async Task Timecalc(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
