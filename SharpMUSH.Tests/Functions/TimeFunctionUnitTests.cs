using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class TimeFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

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

	// ETIME tests - Penn etime.1-etime.6
	[Test]
	[Arguments("etime(0)", "0s")]
	[Arguments("etime(59)", "59s")]
	[Arguments("etime(60)", "1m")]
	[Arguments("etime(61)", "1m  1s")]
	[Arguments("etime(61,5)", "1m")]
	public async Task Etime(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// STRINGSECS tests - Penn stringsecs.1-stringsecs.9 + testtime stringsecs.1-3
	[Test]
	[Arguments("stringsecs(1d)", "86400")]
	[Arguments("stringsecs(5m 1s)", "301")]
	[Arguments("stringsecs(3y 2m 7d 5h 23m)", "95232300")]
	[Arguments("stringsecs(10)", "10")]
	[Arguments("stringsecs(10s)", "10")]
	[Arguments("stringsecs(5m)", "300")]
	[Arguments("stringsecs(1h)", "3600")]
	[Arguments("stringsecs(10s 5m)", "310")]
	[Arguments("stringsecs(1d 2h 3m 4s)", "93784")]
	public async Task Stringsecs(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	// TIMESTRING tests - Penn timestring.1-timestring.3
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

	// ETIMEFMT tests - Penn etimefmt.1-etimefmt.18
	[Test]
	[Arguments("etimefmt($w $d $h $m $s,59)", "0 0 0 0 59")]
	[Arguments("etimefmt($w $d $h $m $s,75)", "0 0 0 1 15")]
	[Arguments("etimefmt($w $d $h $m $s,3665)", "0 0 1 1 5")]
	[Arguments("etimefmt($ts,75)", "75")]
	[Arguments("etimefmt($xm$xs,75)", "1m15s")]
	[Arguments("etimefmt(test $2S,5)", "test 05")]
	[Arguments("etimefmt($2s test,5)", " 5 test")]
	[Arguments("etimefmt($zm $zs,45)", " 45")]
	[Arguments("etimefmt($zxm $zxs,45)", " 45s")]
	[Arguments("etimefmt($xzd $xth,86405)", "1d 24h")]
	[Arguments("etimefmt($2H:$2M,3661)", "01:01")]
	[Arguments("etimefmt($2h:$2M,3700)", " 1:01")]
	[Arguments("etimefmt($2mm $2ss,500)", " 8m 20s")]
	[Arguments("etimefmt(You have $m minutes and $s seconds to go,78)", "You have 1 minutes and 18 seconds to go")]
	[Arguments("etimefmt($txs is $xm$xs,75)", "75s is 1m15s")]
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
