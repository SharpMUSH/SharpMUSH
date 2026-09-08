using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Utilities;
using System.Text;

namespace SharpMUSH.Tests.ConnectionServer;

public class OutputTransformServiceTests
{
	private readonly OutputTransformService _service;

	public OutputTransformServiceTests()
	{
		_service = new OutputTransformService(NullLogger<OutputTransformService>.Instance);
	}

	[Test]
	public async Task TransformAsync_NoTransformation_WhenAnsiEnabledAndSupported()
	{
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("\x1b[31mRed text\x1b[0m");
	}

	/// <summary>
	/// The bug this ladder exists to prevent. Logging in publishes the character's colour flags, and a
	/// character with neither ANSI nor COLOR published <c>false</c> for both — which the transform read
	/// as "this player refuses colour" and stripped every escape the renderer had just produced. The
	/// default <c>player_flags</c> grants <c>ansi</c> and never <c>color</c>, so no character could pass
	/// that gate without being told to set the flags by hand. An unset flag is not a refusal.
	/// </summary>
	[Test]
	public async Task TransformAsync_KeepsColour_WhenFlagsAreUnsetButTerminalClaimsAnsi()
	{
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true);
		var preferences = new PlayerOutputPreferences(
			AnsiEnabled: false, ColorEnabled: false, Xterm256Enabled: false, TruecolorEnabled: false);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("\x1b[31mRed text\x1b[0m");
	}

	/// <summary>
	/// The same fact one rung up: <c>preferences?.TruecolorEnabled ?? capabilities.SupportsTruecolor</c>
	/// reads like a per-flag fallback to MTTS but can never be one, because a non-null preferences
	/// record's <c>false</c> satisfies the <c>??</c>. Every rung silently stopped consulting the
	/// terminal the moment a character logged in.
	/// </summary>
	[Test]
	public async Task TransformAsync_KeepsTruecolor_WhenFlagIsUnsetButTerminalClaimsIt()
	{
		var input = "\x1b[38;2;255;0;0mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: true);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("\x1b[38;2;255;0;0mRed text\x1b[0m");
	}

	/// <summary>
	/// Refusing colour is <c>SOCKSET colorstyle</c>'s job, not a flag's: only a pin renders below what
	/// the client and the flags between them claim.
	/// </summary>
	[Test]
	public async Task TransformAsync_StripsAnsi_WhenColorStylePinnedToPlain()
	{
		var input = "\x1b[1m\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: true, ColorStylePin: ColorStyles.Plain);
		var preferences = new PlayerOutputPreferences(
			AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true, TruecolorEnabled: true);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("Red text");
	}

	/// <summary>PennMUSH's middle rung: the attributes survive and the hues do not.</summary>
	[Test]
	public async Task TransformAsync_PinnedHilite_KeepsAttributesAndDropsColour()
	{
		var input = "\x1b[1;31mBold red\x1b[0m \x1b[4mUnderline\x1b[24m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true, ColorStylePin: ColorStyles.Hilite);

		var result = await _service.TransformAsync(input, capabilities, null);

		await Assert.That(Encoding.UTF8.GetString(result))
			.IsEqualTo("\x1b[1mBold red\x1b[0m \x1b[4mUnderline\x1b[24m");
	}

	/// <summary>
	/// An extended colour carries its own arguments; dropping the <c>38</c> and leaving them behind
	/// would emit the selector as blink and the palette index as a foreground colour.
	/// </summary>
	[Test]
	[Arguments("\x1b[1;38;5;196mText\x1b[0m", "\x1b[1mText\x1b[0m")]
	[Arguments("\x1b[38;2;255;0;0;1mText\x1b[0m", "\x1b[1mText\x1b[0m")]
	[Arguments("\x1b[48;5;21mText\x1b[0m", "Text\x1b[0m")]
	public async Task TransformAsync_PinnedHilite_DropsExtendedColourWithItsArguments(string input, string expected)
	{
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true, ColorStylePin: ColorStyles.Hilite);

		var result = await _service.TransformAsync(Encoding.UTF8.GetBytes(input), capabilities, null);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo(expected);
	}

	/// <summary>An MXP line mode shares CSI syntax with SGR but is not one, and must survive intact.</summary>
	[Test]
	public async Task TransformAsync_PinnedHilite_LeavesMxpLineModesAlone()
	{
		var input = "\x1b[1z\x1b[31mRed\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, Format: OutputFormat.Mxp, ColorStylePin: ColorStyles.Hilite);

		var result = await _service.TransformAsync(input, capabilities, null);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("\x1b[1zRed\x1b[0m");
	}

	/// <summary>A pin renders below the terminal's own claim, which is the whole point of pinning one.</summary>
	[Test]
	public async Task TransformAsync_PinnedSixteenColor_DowngradesATruecolorTerminal()
	{
		var input = "\x1b[38;2;255;0;0mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: true,
			ColorStylePin: ColorStyles.SixteenColor);
		var preferences = new PlayerOutputPreferences(
			AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true, TruecolorEnabled: true);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, preferences));

		await Assert.That(result).DoesNotContain("38;2;");
		await Assert.That(result).DoesNotContain("38;5;");
		await Assert.That(result).Contains("Red text");
	}

	/// <summary>
	/// A pin is the player speaking for themselves, so it also overrides the screen-reader default —
	/// somebody running a screen reader alongside a colour-capable terminal can ask for the colour.
	/// </summary>
	[Test]
	public async Task TransformAsync_PinOverridesScreenReaderDefault()
	{
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: false, ScreenReader: true, ColorStylePin: ColorStyles.SixteenColor);

		var result = await _service.TransformAsync(input, capabilities, null);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("\x1b[31mRed text\x1b[0m");
	}

	/// <summary>
	/// sharpflag.md's split, honoured: ANSI is "this client can highlight", COLOR is "this client can
	/// colour". A player with only the first, on a terminal claiming nothing, gets the attributes.
	/// </summary>
	[Test]
	public async Task TransformAsync_AnsiFlagAlone_RendersHilite()
	{
		var input = "\x1b[1;31mBold red\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: false);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("\x1b[1mBold red\x1b[0m");
	}

	[Test]
	public async Task TransformAsync_PlayerFlagsOverrideInferredAnsiCapability()
	{
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("\x1b[31mRed text\x1b[0m");
	}

	[Test]
	public async Task TransformAsync_ScreenReaderOverridesPlayerColorFlags()
	{
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: false,
			ScreenReader: true);
		var preferences = new PlayerOutputPreferences(
			AnsiEnabled: true,
			ColorEnabled: true,
			Xterm256Enabled: true,
			TruecolorEnabled: true);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		await Assert.That(Encoding.UTF8.GetString(result)).IsEqualTo("Red text");
	}

	[Test]
	public async Task TransformAsync_StripsAnsi_WhenNoPreferences()
	{
		var input = "\x1b[31mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false);

		var result = await _service.TransformAsync(input, capabilities, null);

		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Red text");
	}

	[Test]
	public async Task TransformAsync_DowngradesXterm256_WhenNotSupported()
	{
		var input = "\x1b[38;5;196mBright red\x1b[0m"u8.ToArray(); // 256-color red (196)
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: false);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		var resultText = Encoding.UTF8.GetString(result);
		// 196 in 256-color palette should be downgraded to a 16-color code
		// The regex replaces 38;5;N with 3X where X is the mapped color
		await Assert.That(resultText).Contains("\x1b[3");
		await Assert.That(resultText).Contains("Bright red");
		await Assert.That(resultText).DoesNotContain("38;5;196");
	}

	[Test]
	public async Task TransformAsync_PreservesXterm256_WhenPlayerFlagEnabled()
	{
		var input = "\x1b[38;5;196mBright red\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: true);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).Contains("38;5;196");
	}

	[Test]
	public async Task TransformAsync_PlayerFlagOverridesInferredXterm256Capability()
	{
		var input = "\x1b[38;5;196mBright red\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false, SupportsXterm256: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, preferences));

		await Assert.That(result).IsEqualTo("\x1b[38;5;196mBright red\x1b[0m");
	}

	/// <summary>
	/// The renderer emits 24-bit RGB freely — every hex <c>ansi()</c> code and every syntax-highlighted
	/// help block does — and until the transform knew the sequence existed, those reached a 16-colour
	/// client verbatim: <c>Xterm256ColorRegex</c> matches only <c>38;5;n</c>.
	/// </summary>
	[Test]
	public async Task TransformAsync_PreservesTruecolor_WhenPlayerFlagEnabled()
	{
		var input = "\x1b[38;2;255;0;0mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: true);
		var preferences = new PlayerOutputPreferences(
			AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true, TruecolorEnabled: true);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, preferences));

		await Assert.That(result).IsEqualTo("\x1b[38;2;255;0;0mRed text\x1b[0m");
	}

	[Test]
	public async Task TransformAsync_PlayerFlagOverridesInferredTruecolorCapability()
	{
		var input = "\x1b[38;2;255;0;0mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: false, SupportsXterm256: false, SupportsTruecolor: false);
		var preferences = new PlayerOutputPreferences(
			AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: false, TruecolorEnabled: true);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, preferences));

		await Assert.That(result).IsEqualTo("\x1b[38;2;255;0;0mRed text\x1b[0m");
	}

	[Test]
	public async Task TransformAsync_UsesInferredTruecolorCapabilityBeforeLogin()
	{
		var input = "\x1b[38;2;255;0;0mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: true);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, null));

		await Assert.That(result).IsEqualTo("\x1b[38;2;255;0;0mRed text\x1b[0m");
	}

	[Test]
	public async Task TransformAsync_DowngradesTruecolorToPalette_WhenOnlyXterm256Supported()
	{
		var input = "\x1b[38;2;255;0;0mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, preferences));

		// Pure red is cube entry 16 + 36*5 = 196.
		await Assert.That(result).IsEqualTo("\x1b[38;5;196mRed text\x1b[0m");
	}

	/// <summary>
	/// Colour depth is a ladder: 24-bit has to become a palette entry before the palette entry can
	/// become one of the basic sixteen. Skipping the first rung left the sequence on the wire.
	/// </summary>
	[Test]
	public async Task TransformAsync_DowngradesTruecolorAllTheWay_WhenOnly16ColorSupported()
	{
		var input = "\x1b[38;2;255;0;0mRed text\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: false, SupportsTruecolor: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: false);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, preferences));

		await Assert.That(result).DoesNotContain("38;2;");
		await Assert.That(result).DoesNotContain("38;5;");
		await Assert.That(result).Contains("Red text");
	}

	[Test]
	public async Task TransformAsync_DowngradesTruecolorBackground_AsWellAsForeground()
	{
		var input = "\x1b[48;2;0;0;255mOn blue\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, preferences));

		// Pure blue is cube entry 16 + 5 = 21.
		await Assert.That(result).IsEqualTo("\x1b[48;5;21mOn blue\x1b[0m");
	}

	/// <summary>Near-neutral colours take the 24-step grey ramp, which is finer than the cube.</summary>
	[Test]
	public async Task TransformAsync_MapsGreyToTheGreyRamp_NotTheColourCube()
	{
		var input = "\x1b[38;2;128;130;127mGrey\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(
			SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: false);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true);

		var result = Encoding.UTF8.GetString(await _service.TransformAsync(input, capabilities, preferences));

		// Mean 128 → 232 + (128-8)/10 = 244, inside the 232..255 ramp.
		await Assert.That(result).IsEqualTo("\x1b[38;5;244mGrey\x1b[0m");
	}

	[Test]
	public async Task TransformAsync_ConvertsToAscii_WhenAsciiCharset()
	{
		var input = "Hello © World"u8.ToArray(); // Contains UTF-8 copyright symbol
		var capabilities = new ProtocolCapabilities(SupportsUtf8: false, Charset: "ASCII");

		var result = await _service.TransformAsync(input, capabilities, null);

		var resultText = Encoding.ASCII.GetString(result);
		// ASCII encoding will replace © with ?
		await Assert.That(resultText).Contains("Hello");
		await Assert.That(resultText).Contains("World");
		await Assert.That(resultText).DoesNotContain("©");
	}

	[Test]
	public async Task TransformAsync_PreservesUtf8_WhenUtf8Charset()
	{
		var input = "Hello © World 🎮"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsUtf8: true, Charset: "UTF-8");

		var result = await _service.TransformAsync(input, capabilities, null);

		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Hello © World 🎮");
	}

	[Test]
	public async Task TransformAsync_HandlesComplexAnsi_StripsAll()
	{
		var input = "\x1b[1m\x1b[31mBold Red\x1b[0m \x1b[4m\x1b[32mUnderline Green\x1b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false);

		var result = await _service.TransformAsync(input, capabilities, null);

		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("Bold Red Underline Green");
	}

	[Test]
	public async Task TransformAsync_HandlesInvalidUtf8Gracefully()
	{
		var input = new byte[] { 0xFF, 0xFE, 0xFD }; // Invalid UTF-8
		var capabilities = new ProtocolCapabilities();

		var result = await _service.TransformAsync(input, capabilities, null);

		// service should handle invalid UTF-8 gracefully by using replacement characters
		// The exact output will be UTF-8 replacement characters (U+FFFD = EF BF BD in UTF-8)
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsNotEmpty();
		// UTF-8 encoding converts invalid bytes to replacement character
		await Assert.That(resultText).Contains("\uFFFD");
	}

	[Test]
	public async Task TransformAsync_StripsOsc8Hyperlinks_WhenAnsiEnabled()
	{
		// OSC 8 hyperlink: ESC]8;;url BEL text ESC]8;; BEL
		var input = "See \u001b]8;;help newbie2\u0007newbie2\u001b]8;;\u0007 for more."u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: true);
		var preferences = new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true);

		var result = await _service.TransformAsync(input, capabilities, preferences);

		// OSC 8 should be stripped, preserving display text
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("See newbie2 for more.");
		await Assert.That(resultText).DoesNotContain("]8;;");
	}

	[Test]
	public async Task TransformAsync_StripsOsc8Hyperlinks_WhenAnsiDisabled()
	{
		// OSC 8 hyperlink with additional ANSI formatting
		var input = "\u001b[1m\u001b]8;;help topic\u0007topic text\u001b]8;;\u0007\u001b[0m"u8.ToArray();
		var capabilities = new ProtocolCapabilities(SupportsAnsi: false);

		var result = await _service.TransformAsync(input, capabilities, null);

		// Both ANSI and OSC 8 should be stripped
		var resultText = Encoding.UTF8.GetString(result);
		await Assert.That(resultText).IsEqualTo("topic text");
	}
}
