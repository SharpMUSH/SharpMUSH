using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// What a connection is sent beyond colour: worked out from the terminal's name, its MTTS claims and its
/// answers, with the player's SOCKSET pins over the top. Nothing is sent on a guess.
/// </summary>
public class TerminalFeatureReaderTests
{
	[Test]
	[Arguments("XTERM-KITTY", TerminalOutputFeatures.Hyperlinks | TerminalOutputFeatures.KittyGraphics)]
	[Arguments("xterm-ghostty", TerminalOutputFeatures.Hyperlinks | TerminalOutputFeatures.KittyGraphics)]
	[Arguments("WEZTERM", TerminalOutputFeatures.Hyperlinks | TerminalOutputFeatures.InlineImages)]
	[Arguments("FOOT", TerminalOutputFeatures.Hyperlinks | TerminalOutputFeatures.Sixel)]
	[Arguments("MUDLET", TerminalOutputFeatures.Hyperlinks)]
	[Arguments("XTERM-256COLOR", TerminalOutputFeatures.None)]
	[Arguments("MUSHCLIENT", TerminalOutputFeatures.None)]
	public async Task Detect_ReadsTheTerminalName(string name, TerminalOutputFeatures expected) =>
		await Assert.That(TerminalFeatureReader.Detect([name], null)).IsEqualTo(expected);

	[Test]
	public async Task Detect_ReadsMslpFromMtts() =>
		await Assert.That(TerminalFeatureReader.Detect(["TINTIN++", "ANSI", "MSLP"], null))
			.IsEqualTo(TerminalOutputFeatures.CommandLinks);

	[Test]
	public async Task Detect_ProbeOverridesTheNameForKitty()
	{
		var refused = new TerminalProbeResult(KittyGraphics: false, Sixel: false, Version: null, 0, 0);
		var answered = new TerminalProbeResult(KittyGraphics: true, Sixel: true, Version: "iTerm2 3.5", 0, 0);

		await Assert.That(TerminalFeatureReader.Detect(["XTERM-KITTY"], refused).HasFlag(TerminalOutputFeatures.KittyGraphics))
			.IsFalse().Because("a kitty behind a multiplexer that does not pass the protocol on does not answer");
		await Assert.That(TerminalFeatureReader.Detect(["XTERM-256COLOR"], answered))
			.IsEqualTo(TerminalOutputFeatures.KittyGraphics | TerminalOutputFeatures.Sixel
				| TerminalOutputFeatures.InlineImages | TerminalOutputFeatures.Hyperlinks);
	}

	[Test]
	public async Task Resolve_PinsReplaceWhatWasDetected()
	{
		var detected = TerminalOutputFeatures.Hyperlinks | TerminalOutputFeatures.KittyGraphics;

		await Assert.That(TerminalFeatureReader.Resolve(detected, false, true, TerminalGraphics.Sixel, utf8: true, screenReader: false))
			.IsEqualTo(TerminalOutputFeatures.CommandLinks | TerminalOutputFeatures.Sixel);
		await Assert.That(TerminalFeatureReader.Resolve(detected, null, null, TerminalGraphics.Off, utf8: true, screenReader: false))
			.IsEqualTo(TerminalOutputFeatures.Hyperlinks);
		await Assert.That(TerminalFeatureReader.Resolve(detected, null, null, null, utf8: true, screenReader: false))
			.IsEqualTo(detected);
	}

	[Test]
	public async Task Resolve_KittyAndBlocksNeedUtf8()
	{
		await Assert.That(TerminalFeatureReader.Resolve(TerminalOutputFeatures.KittyGraphics, null, null, null, utf8: false, screenReader: false))
			.IsEqualTo(TerminalOutputFeatures.None);
		await Assert.That(TerminalFeatureReader.Resolve(TerminalOutputFeatures.None, null, null, TerminalGraphics.Blocks, utf8: false, screenReader: false))
			.IsEqualTo(TerminalOutputFeatures.None);
		await Assert.That(TerminalFeatureReader.Resolve(TerminalOutputFeatures.None, null, null, TerminalGraphics.Sixel, utf8: false, screenReader: false))
			.IsEqualTo(TerminalOutputFeatures.Sixel).Because("sixel is ASCII");
	}

	[Test]
	public async Task Resolve_AScreenReaderGetsNoPicturesAndOnlyPinnedLinks() =>
		await Assert.That(TerminalFeatureReader.Resolve(TerminalOutputFeatures.Hyperlinks | TerminalOutputFeatures.KittyGraphics,
				null, true, TerminalGraphics.Kitty, utf8: true, screenReader: true))
			.IsEqualTo(TerminalOutputFeatures.CommandLinks);

	[Test]
	public async Task For_ReadsPinsAndAnswersFromMetadata()
	{
		var metadata = new Dictionary<string, string>
		{
			[TerminalCapabilityReader.TerminalTypesKey] = "XTERM-256COLOR",
			[TerminalFeatureReader.ProbeKittyKey] = "1",
			[TerminalFeatureReader.HyperlinksKey] = "0",
			[TerminalFeatureReader.ProbeCellSizeKey] = "9x18"
		};

		await Assert.That(TerminalFeatureReader.For(metadata)).IsEqualTo(TerminalOutputFeatures.KittyGraphics);
		await Assert.That(TerminalFeatureReader.ProbeOf(metadata))
			.IsEqualTo(new TerminalProbeResult(true, false, null, 9, 18));
		await Assert.That(TerminalFeatureReader.GraphicsName(TerminalOutputFeatures.Sixel | TerminalOutputFeatures.BlockArt))
			.IsEqualTo(TerminalGraphics.Sixel);
	}

	[Test]
	public async Task ProbeOf_ReadsAnUnansweredQuestionAsUnknown()
	{
		// A second report that did not settle Kitty replaces an earlier yes with an empty value.
		var metadata = new Dictionary<string, string>
		{
			[TerminalCapabilityReader.TerminalTypesKey] = "XTERM-256COLOR",
			[TerminalFeatureReader.ProbeKittyKey] = "",
			[TerminalFeatureReader.ProbeSixelKey] = "0",
			[TerminalFeatureReader.ProbeVersionKey] = "",
			[TerminalFeatureReader.ProbeCellSizeKey] = ""
		};

		await Assert.That(TerminalFeatureReader.ProbeOf(metadata))
			.IsEqualTo(new TerminalProbeResult(null, false, null, 0, 0));
		await Assert.That(TerminalFeatureReader.For(metadata)).IsEqualTo(TerminalOutputFeatures.None);
	}
}
