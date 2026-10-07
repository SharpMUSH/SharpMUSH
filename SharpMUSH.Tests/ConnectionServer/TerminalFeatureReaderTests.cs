using MarkupString.Ansi;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// What a connection is sent beyond colour: links worked out from the terminal's name, its MTTS claims and its
/// answers; pictures and moving pictures only once the player turns them on.
/// </summary>
public class TerminalFeatureReaderTests
{
	private const TerminalFeatures Links = TerminalFeatures.Hyperlinks | TerminalFeatures.CommandLinks;

	private static TerminalFeatures Detected(string[] types, TerminalProbeResult? probe = null, string? pin = null) =>
		TerminalFeatureReader.Detect(TerminalFeatureReader.Identify(types, probe, pin), types, probe);

	[Test]
	[Arguments("XTERM-KITTY", "kitty")]
	[Arguments("xterm-ghostty", "ghostty")]
	[Arguments("WEZTERM", "wezterm")]
	[Arguments("FOOT", "foot")]
	[Arguments("XTERM-256COLOR", null)]
	[Arguments("MUSHCLIENT", null)]
	public async Task Identify_ReadsTheTerminalName(string name, string? id) =>
		await Assert.That(TerminalFeatureReader.Identify([name], null, null)?.Id).IsEqualTo(id);

	[Test]
	public async Task Identify_PrefersThePlayersNameThenTheTerminalsAnswer()
	{
		var answered = new TerminalProbeResult(null, false, "iTerm2 3.5.4", 0, 0);

		await Assert.That(TerminalFeatureReader.Identify(["XTERM-KITTY"], answered, null)).IsEqualTo(TerminalProfile.ITerm2);
		await Assert.That(TerminalFeatureReader.Identify(["XTERM-KITTY"], answered, "windows-terminal"))
			.IsEqualTo(TerminalProfile.WindowsTerminal);
		await Assert.That(TerminalFeatureReader.Identify(["XTERM-KITTY"], null, "no-such-terminal")).IsEqualTo(TerminalProfile.Kitty);
	}

	[Test]
	public async Task Detect_ReadsLinksFromTheNameAndMtts()
	{
		await Assert.That(Detected(["XTERM-KITTY"]) & Links).IsEqualTo(TerminalFeatures.Hyperlinks);
		await Assert.That(Detected(["MUDLET"]) & Links).IsEqualTo(TerminalFeatures.Hyperlinks);
		await Assert.That(Detected(["TINTIN++", "ANSI", "MSLP"]) & Links).IsEqualTo(TerminalFeatures.CommandLinks);
		await Assert.That(Detected(["XTERM-256COLOR"])).IsEqualTo(TerminalFeatures.BlockArt);
	}

	[Test]
	public async Task Detect_TheTerminalsAnswerWinsOverItsName()
	{
		var refused = new TerminalProbeResult(KittyGraphics: false, Sixel: false, Version: null, 0, 0);
		var answered = new TerminalProbeResult(KittyGraphics: true, Sixel: true, Version: null, 0, 0);

		await Assert.That(Detected(["XTERM-KITTY"], refused).HasFlag(TerminalFeatures.KittyGraphics))
			.IsFalse().Because("a kitty that does not answer is behind something that drops the protocol");
		await Assert.That(Detected(["XTERM"], refused).HasFlag(TerminalFeatures.Sixel))
			.IsFalse().Because("xterm draws sixel only when started as a VT340");
		await Assert.That(Detected(["XTERM-256COLOR"], answered) & TerminalFeatures.Pictures)
			.IsEqualTo(TerminalFeatures.KittyGraphics | TerminalFeatures.Sixel | TerminalFeatures.BlockArt);
	}

	[Test]
	public async Task Resolve_SendsNoPictureUntilTheyAreTurnedOn()
	{
		var kitty = TerminalProfile.Kitty;
		var detected = TerminalFeatureReader.Detect(kitty, ["XTERM-KITTY"], null);

		await Assert.That(TerminalFeatureReader.Resolve(detected, kitty, TerminalPins.None, utf8: true, screenReader: false))
			.IsEqualTo(TerminalFeatures.Hyperlinks);
		await Assert.That(TerminalFeatureReader.Resolve(detected, kitty, new TerminalPins(Graphics: TerminalGraphics.Auto),
				utf8: true, screenReader: false))
			.IsEqualTo(TerminalFeatures.Hyperlinks | TerminalFeatures.KittyGraphics | TerminalFeatures.BlockArt);
		await Assert.That(TerminalFeatureReader.Resolve(detected, kitty, new TerminalPins(Hyperlinks: false, CommandLinks: true,
				Graphics: TerminalGraphics.Sixel), utf8: true, screenReader: false))
			.IsEqualTo(TerminalFeatures.CommandLinks | TerminalFeatures.Sixel);
	}

	[Test]
	public async Task Resolve_MovingPicturesNeedAnimationAndATerminalThatPlaysThem()
	{
		var animated = new TerminalPins(Graphics: TerminalGraphics.Auto, Animation: true);

		await Assert.That(Resolve(TerminalProfile.Kitty, new TerminalPins(Graphics: TerminalGraphics.Auto))
			.HasFlag(TerminalFeatures.MovingPictures)).IsFalse();
		await Assert.That(Resolve(TerminalProfile.Kitty, animated).HasFlag(TerminalFeatures.MovingPictures)).IsTrue();
		await Assert.That(Resolve(TerminalProfile.Ghostty, animated).HasFlag(TerminalFeatures.MovingPictures))
			.IsFalse().Because("Ghostty does not play Kitty animation");
		await Assert.That(Resolve(TerminalProfile.Foot, animated).HasFlag(TerminalFeatures.MovingPictures))
			.IsFalse().Because("sixel draws one frame");
		await Assert.That(Resolve(null, new TerminalPins(Graphics: TerminalGraphics.Iterm2, Animation: true))
			.HasFlag(TerminalFeatures.MovingPictures)).IsTrue().Because("an unknown terminal is taken at the player's word");

		static TerminalFeatures Resolve(TerminalProfile? terminal, TerminalPins pins) =>
			TerminalFeatureReader.Resolve(TerminalFeatureReader.Detect(terminal, [], null), terminal, pins, utf8: true,
				screenReader: false);
	}

	[Test]
	public async Task Resolve_DropsWhatOutputNotInUtf8CannotCarry()
	{
		await Assert.That(TerminalFeatureReader.Resolve(TerminalFeatures.KittyGraphics | TerminalFeatures.BlockArt, null,
				new TerminalPins(Graphics: TerminalGraphics.Auto), utf8: false, screenReader: false))
			.IsEqualTo(TerminalFeatures.None);
		await Assert.That(TerminalFeatureReader.Resolve(TerminalFeatures.None, null, new TerminalPins(Graphics: TerminalGraphics.Sixel),
				utf8: false, screenReader: false))
			.IsEqualTo(TerminalFeatures.Sixel).Because("sixel is ASCII");
	}

	[Test]
	public async Task Resolve_AScreenReaderGetsOnlyPinnedLinks() =>
		await Assert.That(TerminalFeatureReader.Resolve(TerminalFeatures.Hyperlinks | TerminalFeatures.KittyGraphics, null,
				new TerminalPins(CommandLinks: true, Graphics: TerminalGraphics.Kitty), utf8: true, screenReader: true))
			.IsEqualTo(TerminalFeatures.CommandLinks);

	[Test]
	public async Task For_ReadsTheConnectionsMetadata()
	{
		var metadata = new Dictionary<string, string>
		{
			[TerminalCapabilityReader.TerminalTypesKey] = "XTERM-256COLOR",
			[TerminalFeatureReader.ProbeKittyKey] = "1",
			[TerminalFeatureReader.HyperlinksKey] = "0",
			[TerminalFeatureReader.ProbeCellSizeKey] = "9x18",
			[TerminalFeatureReader.GraphicsKey] = TerminalGraphics.Auto,
			[TerminalFeatureReader.TerminalKey] = "kitty"
		};

		await Assert.That(TerminalFeatureReader.For(metadata))
			.IsEqualTo(TerminalFeatures.KittyGraphics | TerminalFeatures.BlockArt);
		await Assert.That(TerminalFeatureReader.TerminalOf(metadata)).IsEqualTo(TerminalProfile.Kitty);
		await Assert.That(TerminalFeatureReader.ProbeOf(metadata))
			.IsEqualTo(new TerminalProbeResult(true, false, null, 9, 18));
		await Assert.That(TerminalFeatureReader.GraphicsName(TerminalFeatures.Sixel | TerminalFeatures.BlockArt))
			.IsEqualTo(TerminalGraphics.Sixel);
	}

	[Test]
	public async Task ProbeOf_AnUnansweredQuestionIsNotAnAnswer()
	{
		// What the socket server stores when the terminal answered nothing at all.
		var metadata = new Dictionary<string, string>
		{
			[TerminalFeatureReader.ProbeKittyKey] = "",
			[TerminalFeatureReader.ProbeSixelKey] = "0",
			[TerminalFeatureReader.ProbeVersionKey] = "",
			[TerminalFeatureReader.ProbeCellSizeKey] = ""
		};

		await Assert.That(TerminalFeatureReader.ProbeOf(metadata))
			.IsEqualTo(new TerminalProbeResult(null, false, null, 0, 0));
		await Assert.That(TerminalFeatureReader.For(metadata)).IsEqualTo(TerminalFeatures.None);
	}
}
