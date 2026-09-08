using System.Globalization;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Time;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The precision-argument contract from docs/superpowers/specs/2026-09-08-millisecond-precision-design.md:
/// a PennMUSH call keeps PennMUSH's unit, milliseconds are reached only by asking, and every number
/// a function reads is seconds.
/// </summary>
public class TimePrecisionFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code)
		=> (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	// ---- The helper, which every function funnels through --------------------------------------

	[Test]
	[Arguments(null, TimePrecision.Seconds)]
	[Arguments("", TimePrecision.Seconds)]
	[Arguments("s", TimePrecision.Seconds)]
	[Arguments("S", TimePrecision.Seconds)]
	[Arguments("seconds", TimePrecision.Seconds)]
	[Arguments("f", TimePrecision.Fractional)]
	[Arguments("FRACTIONAL", TimePrecision.Fractional)]
	[Arguments("ms", TimePrecision.Milliseconds)]
	[Arguments("Milliseconds", TimePrecision.Milliseconds)]
	public async Task PrecisionTokensParse(string? token, TimePrecision expected)
	{
		await Assert.That(TimePrecisions.TryParse(token, out var precision)).IsTrue();
		await Assert.That(precision).IsEqualTo(expected);
	}

	[Test]
	[Arguments("us")]
	[Arguments("ns")]
	[Arguments("m")]
	[Arguments("nonsense")]
	public async Task UnknownPrecisionTokensAreRejectedRatherThanDefaulted(string token)
		=> await Assert.That(TimePrecisions.TryParse(token, out _)).IsFalse();

	[Test]
	[Arguments(1500L, TimePrecision.Seconds, "1")]
	[Arguments(1500L, TimePrecision.Fractional, "1.5")]
	[Arguments(1500L, TimePrecision.Milliseconds, "1500")]
	[Arguments(1000L, TimePrecision.Fractional, "1")]
	[Arguments(1778518155494L, TimePrecision.Seconds, "1778518155")]
	[Arguments(1778518155494L, TimePrecision.Fractional, "1778518155.494")]
	[Arguments(1778518155494L, TimePrecision.Milliseconds, "1778518155494")]
	public async Task FormatRendersTheStoredMillisecondValue(long ms, TimePrecision precision, string expected)
		=> await Assert.That(TimePrecisions.Format(ms, precision)).IsEqualTo(expected);

	[Test]
	[Arguments("1", 1000L)]
	[Arguments("1.5", 1500L)]
	[Arguments("-1.5", -1500L)]
	[Arguments("1778518155.494", 1778518155494L)]
	[Arguments("0.001", 1L)]
	public async Task SecondsInputMayCarryAFraction(string input, long expectedMs)
	{
		await Assert.That(TimePrecisions.TryParseSeconds(input, out var ms)).IsTrue();
		await Assert.That(ms).IsEqualTo(expectedMs);
	}

	[Test]
	[Arguments("")]
	[Arguments("abc")]
	[Arguments("1e3")]
	[Arguments("1,5")]
	public async Task NonNumericSecondsAreRejected(string input)
		=> await Assert.That(TimePrecisions.TryParseSeconds(input, out _)).IsFalse();

	/// <summary>
	/// A current-culture parse reads "1.5" as 15 under a comma decimal separator, which would make a
	/// game's arithmetic depend on its host's locale.
	/// </summary>
	[Test]
	public async Task SecondsInputIsCultureIndependent()
	{
		var original = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");
			await Assert.That(TimePrecisions.TryParseSeconds("1.5", out var ms)).IsTrue();
			await Assert.That(ms).IsEqualTo(1500L);
			await Assert.That(TimePrecisions.Format(1500L, TimePrecision.Fractional)).IsEqualTo("1.5");
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}

	/// <summary>
	/// Floor, not truncation, because <see cref="DateTimeOffset.ToUnixTimeSeconds"/> floors pre-epoch
	/// instants — a second convention would make csecs() and convsecs() disagree about one moment.
	/// </summary>
	[Test]
	[Arguments(1500L, 1L)]
	[Arguments(-1500L, -2L)]
	[Arguments(-1000L, -1L)]
	[Arguments(0L, 0L)]
	public async Task WholeSecondsFloor(long ms, long expected)
		=> await Assert.That(TimePrecisions.ToWholeSeconds(ms)).IsEqualTo(expected);

	// ---- secs() -------------------------------------------------------------------------------

	[Test]
	public async Task SecsDefaultsToPennSeconds()
	{
		var seconds = long.Parse(await Eval("secs()"));
		var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		await Assert.That(seconds).IsGreaterThan(now - 60).And.IsLessThan(now + 60);
	}

	[Test]
	public async Task SecsInMillisecondsIsThreeDigitsLonger()
	{
		var milliseconds = long.Parse(await Eval("secs(ms)"));
		var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		await Assert.That(milliseconds).IsGreaterThan(now - 60_000).And.IsLessThan(now + 60_000);
	}

	[Test]
	public async Task SecsFractionalCarriesADecimalPoint()
	{
		var result = await Eval("secs(f)");
		await Assert.That(result).Contains(".");
		await Assert.That(decimal.Parse(result, CultureInfo.InvariantCulture)).IsGreaterThan(0m);
	}

	[Test]
	public async Task SecsRejectsAnUnknownPrecision()
		=> await Assert.That(await Eval("secs(us)")).IsEqualTo("#-1 INVALID PRECISION");

	// ---- csecs() / msecs() --------------------------------------------------------------------

	/// <summary>
	/// The idiom this whole change exists for: PennMUSH softcode ages an object with
	/// sub(secs(),csecs(%0)), which is only meaningful when both are in the same unit.
	/// </summary>
	[Test]
	public async Task CsecsIsInTheSameUnitAsSecs()
	{
		var age = long.Parse(await Eval("sub(secs(),csecs(#1))"));
		await Assert.That(age).IsGreaterThanOrEqualTo(0L).And.IsLessThan(60L * 60L * 24L * 365L * 100L);
	}

	[Test]
	public async Task CsecsMillisecondsMatchesTheStoredValue()
	{
		var seconds = long.Parse(await Eval("csecs(#1)"));
		var milliseconds = long.Parse(await Eval("csecs(#1,ms)"));
		await Assert.That(TimePrecisions.ToWholeSeconds(milliseconds)).IsEqualTo(seconds);
	}

	/// <summary>
	/// objid() carries milliseconds, so csecs(&lt;object&gt;,ms) is the field that reconstructs one.
	/// The seconds form deliberately does not — see the design's objid section.
	/// </summary>
	[Test]
	public async Task ObjidCarriesTheMillisecondForm()
	{
		var objid = await Eval("objid(#1)");
		var milliseconds = await Eval("csecs(#1,ms)");
		await Assert.That(objid).IsEqualTo($"#1:{milliseconds}");
	}

	[Test]
	public async Task MsecsIsInTheSameUnitAsSecs()
	{
		var seconds = long.Parse(await Eval("msecs(#1)"));
		var milliseconds = long.Parse(await Eval("msecs(#1,ms)"));
		await Assert.That(TimePrecisions.ToWholeSeconds(milliseconds)).IsEqualTo(seconds);
	}

	/// <summary>
	/// msecs() and mtime() reported the creation time whenever a second argument was truthy. The
	/// precision argument now occupies that slot, so the only remaining question is that they read
	/// ModifiedTime at all.
	/// </summary>
	/// <remarks>
	/// ModifiedTime is currently only ever written at creation, so it equals CreationTime for every
	/// object; this asserts the two are read from their own fields, not that they diverge.
	/// </remarks>
	[Test]
	public async Task MsecsReadsModificationTime()
	{
		var created = long.Parse(await Eval("csecs(#1,ms)"));
		var modified = long.Parse(await Eval("msecs(#1,ms)"));
		await Assert.That(modified).IsGreaterThanOrEqualTo(created);
	}

	// ---- ctime() / mtime() --------------------------------------------------------------------

	/// <summary>PennMUSH returns a time string from both branches; the utc branch returned a raw number.</summary>
	[Test]
	[Arguments("ctime(#1)")]
	[Arguments("ctime(#1,1)")]
	[Arguments("mtime(#1)")]
	[Arguments("mtime(#1,1)")]
	public async Task CtimeAndMtimeAlwaysReturnATimeString(string code)
	{
		var result = await Eval(code);
		await Assert.That(long.TryParse(result, out _)).IsFalse();
		await Assert.That(result.Split(' ').Length).IsEqualTo(5);
	}

	// ---- consumers read seconds ---------------------------------------------------------------

	[Test]
	[Arguments("timestring(90)", "1m  30s")]
	[Arguments("timestring(90.25)", "1m  30s")]
	[Arguments("timestring(90.25,0,ms)", "1m  30.250s")]
	[Arguments("timestring(90.5,0,f)", "1m  30.5s")]
	public async Task TimestringReadsFractionalSecondsAndRendersAtPrecision(string code, string expected)
		=> await Assert.That((await Eval(code)).Trim()).IsEqualTo(expected.Trim());

	/// <summary>
	/// PennMUSH's fun_etime rejects a negative (src/funtime.c), as timestring() and etimefmt()
	/// already did here. etime() rendered "-1s".
	/// </summary>
	[Test]
	public async Task EtimeRejectsNegativeSeconds()
		=> await Assert.That(await Eval("etime(-1)")).IsEqualTo("#-1 SECONDS MUST NOT BE NEGATIVE");

	/// <summary>
	/// The two parts are read back as one signed magnitude, so -1.5 splits as -1 and 500, not the
	/// -2 and 500 that flooring would give.
	/// </summary>
	[Test]
	[Arguments("1.5", 1L, 500)]
	[Arguments("-1.5", -1L, 500)]
	[Arguments("-2", -2L, 0)]
	[Arguments("0.999", 0L, 999)]
	public async Task SecondsPartsTruncateTowardsZero(string input, long expectedSeconds, int expectedMs)
	{
		await Assert.That(TimePrecisions.TryParseSecondsParts(input, out var seconds, out var ms)).IsTrue();
		await Assert.That(seconds).IsEqualTo(expectedSeconds);
		await Assert.That(ms).IsEqualTo(expectedMs);
	}

	/// <summary>
	/// A millisecond long only reaches ~292 million years, so the duration renderers split into a
	/// seconds part and a remainder rather than collapsing to one number — timestring() documents
	/// and tests the full 64-bit seconds range.
	/// </summary>
	[Test]
	public async Task DurationsKeepTheFull64BitSecondsRange()
		=> await Assert.That(await Eval("timestring(9223372036854775807)"))
			.IsEqualTo(" 106751991167300d  15h  30m  7s");

	[Test]
	[Arguments("etime(61)", "1m  1s")]
	[Arguments("etime(61.5)", "1m  1s")]
	[Arguments("etime(61.5,,ms)", "1m  1.500s")]
	public async Task EtimeReadsFractionalSeconds(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	[Test]
	[Arguments("etimefmt($s,61)", "1")]
	[Arguments("etimefmt($ts,61)", "61")]
	[Arguments("etimefmt($s,61.5,ms)", "1.500")]
	public async Task EtimefmtRendersAtPrecision(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary>
	/// isdaylight() read its PennMUSH-seconds argument through FromUnixTimeMilliseconds, so it
	/// answered for January 1970 whatever it was asked.
	/// </summary>
	[Test]
	public async Task IsdaylightReadsSeconds()
	{
		// 2026-07-01T12:00:00Z, inside northern-hemisphere DST for a zone that observes it.
		var result = await Eval("isdaylight(1782043200,America/Chicago)");
		await Assert.That(result).IsEqualTo("1");
	}

	[Test]
	public async Task IsdaylightIsFalseInWinter()
	{
		// 2026-01-01T12:00:00Z
		var result = await Eval("isdaylight(1767268800,America/Chicago)");
		await Assert.That(result).IsEqualTo("0");
	}

	[Test]
	public async Task TimefmtReadsSeconds()
		=> await Assert.That(await Eval("timefmt($Y,1778518155,UTC)")).IsEqualTo("2026");

	[Test]
	public async Task TimefmtHonoursAFractionalSecond()
		=> await Assert.That(await Eval("timefmt($Y,1778518155.494,UTC)")).IsEqualTo("2026");

	[Test]
	public async Task ConvsecsReadsSeconds()
		=> await Assert.That(await Eval("convutcsecs(709395750)")).IsEqualTo("Wed Jun 24 14:22:30 1992");

	// ---- stringsecs() -------------------------------------------------------------------------

	[Test]
	[Arguments("stringsecs(1.5s)", "1")]
	[Arguments("stringsecs(1.5s,ms)", "1500")]
	[Arguments("stringsecs(1.5s,f)", "1.5")]
	[Arguments("stringsecs(5m 1s,ms)", "301000")]
	public async Task StringsecsRendersAtPrecision(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	// ---- uptime() / starttime() / restarttime() -----------------------------------------------

	/// <summary>PennMUSH's uptime() is seconds for every type; the named types returned milliseconds.</summary>
	[Test]
	[Arguments("uptime()")]
	[Arguments("uptime(upsince)")]
	[Arguments("uptime(reboot)")]
	public async Task UptimeIsSecondsForEveryType(string code)
	{
		var value = long.Parse(await Eval(code));
		var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		await Assert.That(value).IsGreaterThan(0L).And.IsLessThan(now + 60);
	}

	[Test]
	public async Task UptimeInMillisecondsIsAvailableByAsking()
	{
		var seconds = long.Parse(await Eval("uptime(upsince)"));
		var milliseconds = long.Parse(await Eval("uptime(upsince,ms)"));
		await Assert.That(TimePrecisions.ToWholeSeconds(milliseconds)).IsEqualTo(seconds);
	}

	/// <summary>PennMUSH returns -1 for a save that has not happened.</summary>
	[Test]
	public async Task UptimeReportsMinusOneForAnUnsetEvent()
		=> await Assert.That(await Eval("uptime(save)")).IsEqualTo("-1");

	/// <summary>PennMUSH's starttime() and restarttime() are in time() format, not a numeric one.</summary>
	[Test]
	[Arguments("starttime()")]
	[Arguments("restarttime()")]
	public async Task StarttimeAndRestarttimeAreTimeStrings(string code)
	{
		var result = await Eval(code);
		await Assert.That(long.TryParse(result, out _)).IsFalse();
		await Assert.That(result.Split(' ').Length).IsEqualTo(5);
	}
}
