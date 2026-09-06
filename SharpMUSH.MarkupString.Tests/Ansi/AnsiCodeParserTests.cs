using MarkupString.Ansi;

public class AnsiCodeParserTests
{
	[Test]
	public async Task RedCode_SetsForegroundStandardRed()
	{
		var style = AnsiCodeParser.Parse("r").Style;
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Standard(1, false));
		await Assert.That(style.Background).IsNull();
	}

	[Test]
	[Arguments("x", (byte)0)]
	[Arguments("r", (byte)1)]
	[Arguments("g", (byte)2)]
	[Arguments("y", (byte)3)]
	[Arguments("b", (byte)4)]
	[Arguments("m", (byte)5)]
	[Arguments("c", (byte)6)]
	[Arguments("w", (byte)7)]
	public async Task LetterCodes_MapToStandardIndexes(string code, byte index)
	{
		await Assert.That(AnsiCodeParser.Parse(code).Style.Foreground)
			.IsEqualTo(new AnsiColor.Standard(index, false));
	}

	[Test]
	[Arguments("X", (byte)0)]
	[Arguments("R", (byte)1)]
	[Arguments("G", (byte)2)]
	[Arguments("Y", (byte)3)]
	[Arguments("B", (byte)4)]
	[Arguments("M", (byte)5)]
	[Arguments("C", (byte)6)]
	[Arguments("W", (byte)7)]
	public async Task UppercaseLetterCodes_MapToBackgroundStandardIndexes(string code, byte index)
	{
		var style = AnsiCodeParser.Parse(code).Style;
		await Assert.That(style.Background).IsEqualTo(new AnsiColor.Standard(index, false));
		await Assert.That(style.Foreground).IsNull();
	}

	[Test]
	public async Task Highlight_RaisesTheFollowingColourToItsBrightTwin()
	{
		await Assert.That(AnsiCodeParser.Parse("hr").Style.Foreground)
			.IsEqualTo(new AnsiColor.Standard(1, true));
		await Assert.That(AnsiCodeParser.Parse("hR").Style.Background)
			.IsEqualTo(new AnsiColor.Standard(1, true));
	}

	[Test]
	public async Task Highlight_IsPerTokenAndCancelledByUppercaseH()
	{
		// h applies for the rest of its own token only.
		var separate = AnsiCodeParser.Parse("h r").Style;
		await Assert.That(separate.Foreground).IsEqualTo(new AnsiColor.Standard(1, false));

		var cancelled = AnsiCodeParser.Parse("hHr").Style;
		await Assert.That(cancelled.Foreground).IsEqualTo(new AnsiColor.Standard(1, false));
	}

	[Test]
	public async Task DefaultCodes_ProduceTheDefaultColour()
	{
		await Assert.That(AnsiCodeParser.Parse("d").Style.Foreground).IsEqualTo(AnsiColor.Default.Instance);
		await Assert.That(AnsiCodeParser.Parse("D").Style.Background).IsEqualTo(AnsiColor.Default.Instance);
		// Highlight has no bright variant of "default".
		await Assert.That(AnsiCodeParser.Parse("hd").Style.Foreground).IsEqualTo(AnsiColor.Default.Instance);
	}

	[Test]
	[Arguments("200")]
	[Arguments("+xterm200")]
	public async Task XtermCodes_SetForegroundXterm(string code)
	{
		var style = AnsiCodeParser.Parse(code).Style;
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Xterm(200));
		await Assert.That(style.Background).IsNull();
	}

	[Test]
	[Arguments("/200")]
	[Arguments("/+xterm200")]
	public async Task SlashPrefix_TargetsTheBackground(string code)
	{
		var style = AnsiCodeParser.Parse(code).Style;
		await Assert.That(style.Background).IsEqualTo(new AnsiColor.Xterm(200));
		await Assert.That(style.Foreground).IsNull();
	}

	[Test]
	[Arguments("256")]
	[Arguments("-1")]
	[Arguments("+xterm256")]
	[Arguments("+xterm")]
	public async Task OutOfRangeXterm_IsIgnored(string code)
	{
		// A "+xterm" token is always consumed as an xterm token. Letting it fall through to the
		// single-letter branch (as the old parser did) painted "+xterm256" magenta, because the
		// x/r/m of the literal prefix were read as colour letters.
		var style = AnsiCodeParser.Parse(code).Style;
		await Assert.That(style.Foreground).IsNull();
		await Assert.That(style.Background).IsNull();
	}

	[Test]
	public async Task HexColour_SetsForegroundRgb()
	{
		await Assert.That(AnsiCodeParser.Parse("#ff0000").Style.Foreground)
			.IsEqualTo(new AnsiColor.Rgb(255, 0, 0));
		await Assert.That(AnsiCodeParser.Parse("#f00").Style.Foreground)
			.IsEqualTo(new AnsiColor.Rgb(255, 0, 0));
	}

	[Test]
	public async Task BackgroundHexColour_SetsBackgroundOnly()
	{
		var style = AnsiCodeParser.Parse("/#00ff00").Style;
		await Assert.That(style.Background).IsEqualTo(new AnsiColor.Rgb(0, 255, 0));
		await Assert.That(style.Foreground).IsNull();
	}

	[Test]
	[Arguments("#zz")]
	[Arguments("#")]
	[Arguments("#nothex")]
	public async Task InvalidHex_IsIgnoredWithoutThrowing(string code)
	{
		var style = AnsiCodeParser.Parse(code).Style;
		await Assert.That(style.Foreground).IsNull();
		await Assert.That(style.Background).IsNull();
	}

	[Test]
	public async Task RgbTriplet_SetsForegroundRgb()
	{
		await Assert.That(AnsiCodeParser.Parse("<255 0 0>").Style.Foreground)
			.IsEqualTo(new AnsiColor.Rgb(255, 0, 0));
		// The background form needs the tokenizer to keep "/<…>" in one piece; the old tokenizer
		// only looked for a leading '<', so "/<0 0 255>" fragmented into three junk tokens.
		await Assert.That(AnsiCodeParser.Parse("/<0 0 255>").Style.Background)
			.IsEqualTo(new AnsiColor.Rgb(0, 0, 255));
	}

	[Test]
	[Arguments("<255 0>")]
	[Arguments("<256 0 0>")]
	[Arguments("<1 2 3 4>")]
	[Arguments("<a b c>")]
	public async Task InvalidRgbTriplet_IsIgnored(string code)
	{
		var style = AnsiCodeParser.Parse(code).Style;
		await Assert.That(style.Foreground).IsNull();
		await Assert.That(style.Background).IsNull();
	}

	[Test]
	public async Task AttributeCodes_SetTheirFlags()
	{
		await Assert.That(AnsiCodeParser.Parse("u").Style.Underlined).IsTrue();
		await Assert.That(AnsiCodeParser.Parse("i").Style.Inverted).IsTrue();
		await Assert.That(AnsiCodeParser.Parse("f").Style.Blink).IsTrue();
		await Assert.That(AnsiCodeParser.Parse("f").Style.Foreground).IsNull();
	}

	[Test]
	public async Task UppercaseAttributeCodes_ClearTheirFlags()
	{
		await Assert.That(AnsiCodeParser.Parse("uU").Style.Underlined).IsFalse();
		await Assert.That(AnsiCodeParser.Parse("iI").Style.Inverted).IsFalse();
		await Assert.That(AnsiCodeParser.Parse("fF").Style.Blink).IsFalse();
	}

	[Test]
	[Arguments("run")]
	[Arguments("rn")]
	[Arguments("n")]
	public async Task NormalCode_ClearsEverythingAndSetsClear(string code)
	{
		var style = AnsiCodeParser.Parse(code).Style;
		await Assert.That(style.Clear).IsTrue();
		await Assert.That(style.Foreground).IsNull();
		await Assert.That(style.Background).IsNull();
		await Assert.That(style.Underlined).IsFalse();
		await Assert.That(style.Inverted).IsFalse();
		await Assert.That(style.Blink).IsFalse();
	}

	[Test]
	public async Task NormalCode_DoesNotSuppressLaterCodes()
	{
		var style = AnsiCodeParser.Parse("n hg").Style;
		await Assert.That(style.Clear).IsTrue();
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Standard(2, true));
	}

	[Test]
	public async Task MultipleTokens_Accumulate()
	{
		var style = AnsiCodeParser.Parse("hr /#0000ff u").Style;
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Standard(1, true));
		await Assert.That(style.Background).IsEqualTo(new AnsiColor.Rgb(0, 0, 255));
		await Assert.That(style.Underlined).IsTrue();
	}

	[Test]
	public async Task LaterColourWins()
	{
		await Assert.That(AnsiCodeParser.Parse("r g").Style.Foreground)
			.IsEqualTo(new AnsiColor.Standard(2, false));
		await Assert.That(AnsiCodeParser.Parse("r 200").Style.Foreground)
			.IsEqualTo(new AnsiColor.Xterm(200));
	}

	[Test]
	[Arguments("")]
	[Arguments("   ")]
	[Arguments("qQ")]
	public async Task EmptyOrUnknownCodes_ProduceTheEmptyStyle(string code)
	{
		await Assert.That(AnsiCodeParser.Parse(code).Style).IsEqualTo(AnsiStyle.None);
	}

	[Test]
	public async Task UnclosedRgbTriplet_FallsBackToPlainSpaceSplitting()
	{
		// No closing '>', so "<255 0 0" is three ordinary tokens: "<255" (unknown, ignored)
		// followed by two bare integers, the last of which lands as xterm 0.
		var style = AnsiCodeParser.Parse("<255 0 0").Style;
		await Assert.That(style.Foreground).IsEqualTo(new AnsiColor.Xterm(0));
		await Assert.That(style.Background).IsNull();
	}

	[Test]
	public async Task Parse_IsDeterministicAndValueEqual()
	{
		await Assert.That(AnsiCodeParser.Parse("hr")).IsEqualTo(AnsiCodeParser.Parse("hr"));
	}
}
