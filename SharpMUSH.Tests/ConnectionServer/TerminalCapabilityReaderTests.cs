using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// What a client's RFC 1091 terminal types are read to mean. MTTS is the only place a telnet client
/// can say it renders more than 16 colours, and until it was read every telnet connection had
/// <c>SupportsXterm256</c> stuck at its default of false — so the output transform downgraded every
/// xterm256 sequence the game produced, for clients that had said they could display them.
/// </summary>
public class TerminalCapabilityReaderTests
{
	[Test]
	public async Task NoTerminalTypes_AssumesColourButNotDepth()
	{
		var capabilities = TerminalCapabilityReader.Read([]);

		await Assert.That(capabilities).IsEqualTo(TerminalCapabilities.Unknown);
		await Assert.That(capabilities.ColorStyle).IsEqualTo(ColorStyles.SixteenColor)
			.Because("a client that was never asked is not thereby monochrome, nor thereby xterm256");
	}

	/// <summary>
	/// The list TelnetNegotiationCore hands over for an MTTS client: name first, terminal type second,
	/// and the bitvector already expanded into its capability names.
	/// </summary>
	[Test]
	public async Task MttsCapabilityNames_AreRead()
	{
		var capabilities = TerminalCapabilityReader.Read(
			["SharpMUTerm", "XTERM", "ANSI", "UTF8", "256 COLORS", "TRUECOLOR"]);

		await Assert.That(capabilities.Ansi).IsTrue();
		await Assert.That(capabilities.Xterm256).IsTrue();
		await Assert.That(capabilities.Truecolor).IsTrue();
		await Assert.That(capabilities.Utf8).IsTrue();
		await Assert.That(capabilities.ColorStyle).IsEqualTo(ColorStyles.Truecolor);
	}

	/// <summary>
	/// MTTS keeps 256 COLORS and TRUECOLOR apart, and so must we: sending ESC[38;2;r;g;b to a client
	/// that only claimed the palette produces garbage, not an approximation.
	/// </summary>
	[Test]
	public async Task Mtts_PaletteWithoutTruecolor_StaysAtXterm256()
	{
		var capabilities = TerminalCapabilityReader.Read(["SomeClient", "ANSI", "256 COLORS"]);

		await Assert.That(capabilities.Xterm256).IsTrue();
		await Assert.That(capabilities.Truecolor).IsFalse();
		await Assert.That(capabilities.ColorStyle).IsEqualTo(ColorStyles.Xterm256);
	}

	/// <summary>
	/// The implication runs one way only: every terminal that implemented direct colour already had
	/// the palette, so a TRUECOLOR claim carries 256 with it even when that bit is not set.
	/// </summary>
	[Test]
	public async Task Mtts_TruecolorImpliesThePalette()
	{
		var capabilities = TerminalCapabilityReader.Read(["SomeClient", "TRUECOLOR"]);

		await Assert.That(capabilities.Truecolor).IsTrue();
		await Assert.That(capabilities.Xterm256).IsTrue();
		await Assert.That(capabilities.Ansi).IsTrue();
	}

	[Test]
	public async Task Mtts_AnsiWithoutDeepColour_Is16Color()
	{
		var capabilities = TerminalCapabilityReader.Read(["SomeClient", "ANSI"]);

		await Assert.That(capabilities.Ansi).IsTrue();
		await Assert.That(capabilities.Xterm256).IsFalse();
		await Assert.That(capabilities.Utf8).IsFalse();
		await Assert.That(capabilities.ColorStyle).IsEqualTo(ColorStyles.SixteenColor);
	}

	/// <summary>
	/// A screen reader is told plain text however much colour it claims: MTTS defines the bit so a
	/// server can stop sending escapes that get read out loud.
	/// </summary>
	[Test]
	public async Task ScreenReader_OverridesEveryColourClaim()
	{
		var capabilities = TerminalCapabilityReader.Read(["Reader", "ANSI", "256 COLORS", "SCREEN_READER"]);

		await Assert.That(capabilities.ScreenReader).IsTrue();
		await Assert.That(capabilities.ColorStyle).IsEqualTo(ColorStyles.Plain);
	}

	/// <summary>Most terminals never send MTTS — the termcap name is the whole claim.</summary>
	[Test]
	[Arguments("xterm-256color", ColorStyles.Xterm256)]
	[Arguments("screen-256color", ColorStyles.Xterm256)]
	[Arguments("tmux-256color", ColorStyles.Xterm256)]
	[Arguments("xterm-direct", ColorStyles.Truecolor)]
	[Arguments("vte-direct", ColorStyles.Truecolor)]
	[Arguments("xterm-color", ColorStyles.SixteenColor)]
	[Arguments("ansi", ColorStyles.SixteenColor)]
	[Arguments("linux", ColorStyles.SixteenColor)]
	[Arguments("vt100", ColorStyles.SixteenColor)]
	[Arguments("dumb", ColorStyles.Hilite)]
	[Arguments("xterm-mono", ColorStyles.Hilite)]
	[Arguments("UNKNOWN", ColorStyles.Hilite)]
	public async Task TermcapName_ImpliesItsColourDepth(string terminalType, string expected)
		=> await Assert.That(TerminalCapabilityReader.Read([terminalType]).ColorStyle).IsEqualTo(expected);

	[Test]
	public async Task ColorStyleFor_PrefersAnExplicitPinOverWhatTheClientClaims()
	{
		var metadata = new Dictionary<string, string>
		{
			[TerminalCapabilityReader.TerminalTypesKey] = "SharpMUTerm\t256 COLORS\tTRUECOLOR",
			[TerminalCapabilityReader.ColorStyleKey] = ColorStyles.Plain
		};

		await Assert.That(TerminalCapabilityReader.ColorStyleFor(metadata)).IsEqualTo(ColorStyles.Plain)
			.Because("SOCKSET colorstyle is the player overriding what negotiation guessed");
	}

	/// <summary>
	/// The stored list is tab-separated precisely because "256 COLORS" contains a space; splitting it
	/// on whitespace would turn one capability into two entries that mean nothing.
	/// </summary>
	[Test]
	public async Task ColorStyleFor_ReadsTheStoredListWithoutSplittingCapabilityNames()
	{
		var metadata = new Dictionary<string, string>
		{
			[TerminalCapabilityReader.TerminalTypesKey] = "SharpMUTerm\tXTERM\t256 COLORS"
		};

		await Assert.That(TerminalCapabilityReader.ColorStyleFor(metadata)).IsEqualTo(ColorStyles.Xterm256);
	}

	[Test]
	public async Task ColorStyleFor_EmptyMetadata_IsTheUnknownDefault()
		=> await Assert.That(TerminalCapabilityReader.ColorStyleFor(new Dictionary<string, string>()))
			.IsEqualTo(ColorStyles.SixteenColor);
}
