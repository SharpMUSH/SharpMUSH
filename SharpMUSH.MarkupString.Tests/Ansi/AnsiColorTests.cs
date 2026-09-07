using MarkupString.Ansi;

public class AnsiColorTests
{
	[Test]
	public async Task Standard_Normal_UsesVgaPalette()
	{
		await Assert.That(new AnsiColor.Standard(1, false).ToRgb()).IsEqualTo(new AnsiColor.Rgb(170, 0, 0));
		await Assert.That(new AnsiColor.Standard(0, false).ToRgb()).IsEqualTo(new AnsiColor.Rgb(0, 0, 0));
		await Assert.That(new AnsiColor.Standard(3, false).ToRgb()).IsEqualTo(new AnsiColor.Rgb(170, 85, 0));
		await Assert.That(new AnsiColor.Standard(7, false).ToRgb()).IsEqualTo(new AnsiColor.Rgb(170, 170, 170));
	}

	[Test]
	public async Task Standard_Bright_UsesBrightVgaPalette()
	{
		await Assert.That(new AnsiColor.Standard(1, true).ToRgb()).IsEqualTo(new AnsiColor.Rgb(255, 85, 85));
		await Assert.That(new AnsiColor.Standard(0, true).ToRgb()).IsEqualTo(new AnsiColor.Rgb(85, 85, 85));
		await Assert.That(new AnsiColor.Standard(7, true).ToRgb()).IsEqualTo(new AnsiColor.Rgb(255, 255, 255));
	}

	[Test]
	public async Task Standard_IndexAboveSeven_Throws()
		=> await Assert.That(() => new AnsiColor.Standard(8, false)).Throws<ArgumentOutOfRangeException>();

	/// <summary>
	/// A <c>with</c> expression copies the backing field and then runs the <c>init</c> accessor, so
	/// the check has to live in the accessor: an initialiser on the property would only ever see the
	/// constructor's argument, and this would build a colour whose index indexes past the palette.
	/// </summary>
	[Test]
	public async Task Standard_IndexAboveSevenThroughWith_Throws()
	{
		var colour = new AnsiColor.Standard(1, false);

		await Assert.That(() => colour with { Index = 200 }).Throws<ArgumentOutOfRangeException>();
	}

	[Test]
	public async Task Xterm_CubeEntry_ResolvesThroughSixLevelCube()
	{
		// 200 - 16 = 184 => r = 184/36 = 5 (255), g = (184/6)%6 = 0 (0), b = 184%6 = 4 (215)
		await Assert.That(new AnsiColor.Xterm(200).ToRgb()).IsEqualTo(new AnsiColor.Rgb(255, 0, 215));
		await Assert.That(new AnsiColor.Xterm(16).ToRgb()).IsEqualTo(new AnsiColor.Rgb(0, 0, 0));
		await Assert.That(new AnsiColor.Xterm(231).ToRgb()).IsEqualTo(new AnsiColor.Rgb(255, 255, 255));
	}

	[Test]
	public async Task Xterm_GreyRamp_StartsAtEightAndStepsByTen()
	{
		await Assert.That(new AnsiColor.Xterm(232).ToRgb()).IsEqualTo(new AnsiColor.Rgb(8, 8, 8));
		await Assert.That(new AnsiColor.Xterm(255).ToRgb()).IsEqualTo(new AnsiColor.Rgb(238, 238, 238));
	}

	[Test]
	public async Task Xterm_LowIndexes_MatchTheStandardPalette()
	{
		await Assert.That(new AnsiColor.Xterm(9).ToRgb()).IsEqualTo(new AnsiColor.Standard(1, true).ToRgb());
		await Assert.That(new AnsiColor.Xterm(1).ToRgb()).IsEqualTo(new AnsiColor.Standard(1, false).ToRgb());
		await Assert.That(new AnsiColor.Xterm(15).ToRgb()).IsEqualTo(new AnsiColor.Standard(7, true).ToRgb());
	}

	[Test]
	public async Task Default_HasNoRgb()
	{
		await Assert.That(AnsiColor.Default.Instance.ToRgb()).IsNull();
		await Assert.That(AnsiColor.Default.Instance.ToHex()).IsEqualTo(string.Empty);
	}

	[Test]
	public async Task Default_InstancesAreValueEqual()
	{
		await Assert.That(new AnsiColor.Default()).IsEqualTo(AnsiColor.Default.Instance);
	}

	[Test]
	public async Task Rgb_ToRgb_IsItself()
	{
		var rgb = new AnsiColor.Rgb(1, 2, 3);
		await Assert.That(rgb.ToRgb()).IsEqualTo(rgb);
	}

	[Test]
	public async Task ToHex_IsLowercaseSixDigits()
	{
		await Assert.That(new AnsiColor.Rgb(255, 0, 215).ToHex()).IsEqualTo("#ff00d7");
		await Assert.That(new AnsiColor.Rgb(0, 10, 0).ToHex()).IsEqualTo("#000a00");
		await Assert.That(new AnsiColor.Standard(1, false).ToHex()).IsEqualTo("#aa0000");
	}

	[Test]
	public async Task TryParseHex_ShortForm_DoublesEachDigit()
	{
		var ok = AnsiColor.TryParseHex("#abc", out var rgb);
		await Assert.That(ok).IsTrue();
		await Assert.That(rgb).IsEqualTo(new AnsiColor.Rgb(0xaa, 0xbb, 0xcc));
	}

	[Test]
	public async Task TryParseHex_LongForm_IsCaseInsensitive()
	{
		await Assert.That(AnsiColor.TryParseHex("#FF00d7", out var rgb)).IsTrue();
		await Assert.That(rgb).IsEqualTo(new AnsiColor.Rgb(255, 0, 215));
	}

	[Test]
	[Arguments("")]
	[Arguments("#")]
	[Arguments("#zz")]
	[Arguments("#gggggg")]
	[Arguments("#12345")]
	[Arguments("#1234567")]
	[Arguments("ff0000")]
	public async Task TryParseHex_Invalid_ReturnsFalseWithoutThrowing(string input)
	{
		await Assert.That(AnsiColor.TryParseHex(input, out var rgb)).IsFalse();
		await Assert.That(rgb).IsEqualTo(new AnsiColor.Rgb(0, 0, 0));
	}

	[Test]
	public async Task NearestStandard_PicksTheClosestRedmeanEntry()
	{
		// (250,10,10) is nearer to normal red (170,0,0) than to bright red (255,85,85):
		// redmean distance 18667.6 versus 33879.5 - the green/blue gap dominates.
		await Assert.That(AnsiColor.NearestStandard(new AnsiColor.Rgb(250, 10, 10)))
			.IsEqualTo(new AnsiColor.Standard(1, false));
		await Assert.That(AnsiColor.NearestStandard(new AnsiColor.Rgb(255, 80, 80)))
			.IsEqualTo(new AnsiColor.Standard(1, true));
	}

	[Test]
	public async Task NearestStandard_ExactPaletteEntry_RoundTrips()
	{
		await Assert.That(AnsiColor.NearestStandard(new AnsiColor.Rgb(0, 170, 170)))
			.IsEqualTo(new AnsiColor.Standard(6, false));
		await Assert.That(AnsiColor.NearestStandard(new AnsiColor.Rgb(255, 255, 255)))
			.IsEqualTo(new AnsiColor.Standard(7, true));
	}

	[Test]
	public async Task NearestXterm_ExactPaletteEntry_RoundTrips()
	{
		await Assert.That(AnsiColor.NearestXtermIndex(new AnsiColor.Rgb(255, 0, 215)))
			.IsEqualTo(new AnsiColor.Xterm(200));
		await Assert.That(AnsiColor.NearestXtermIndex(new AnsiColor.Rgb(8, 8, 8)))
			.IsEqualTo(new AnsiColor.Xterm(232));
		await Assert.That(AnsiColor.NearestXterm(new AnsiColor.Rgb(255, 0, 215)))
			.IsEqualTo(new AnsiColor.Rgb(255, 0, 215));
	}

	[Test]
	public async Task NearestXterm_OffPaletteColour_ReturnsThePaletteRgb()
	{
		var nearest = AnsiColor.NearestXtermIndex(new AnsiColor.Rgb(250, 10, 10));
		await Assert.That(nearest).IsEqualTo(new AnsiColor.Xterm(196));
		await Assert.That(AnsiColor.NearestXterm(new AnsiColor.Rgb(250, 10, 10)))
			.IsEqualTo(new AnsiColor.Rgb(255, 0, 0));
	}

	[Test]
	public async Task Palette_MatchesColourResolution()
	{
		await Assert.That(AnsiPalette.Standard(4, true)).IsEqualTo(new AnsiColor.Rgb(85, 85, 255));
		await Assert.That(AnsiPalette.Xterm(232)).IsEqualTo(new AnsiColor.Rgb(8, 8, 8));
	}
}
