using SharpMUSH.SocketServer.ProtocolHandlers;
using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using MarkupString.Layout;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Services;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Covers the wire-format rendering that moved out of NotifyService: serialized markup is rendered
/// to ANSI/Pueblo/MXP for terminal connections (per <see cref="ProtocolCapabilities.Format"/>) and
/// forwarded as a markup envelope for WebSocket (portal) connections.
/// </summary>
public partial class MarkupOutputRendererTests
{
	private const string Raw = "<send href=\"look\">Tom & \"Sue\"</send>";

	private static string StripAnsi(string text) => AnsiEscape().Replace(text, string.Empty);

	[GeneratedRegex("\u001b\\[[0-9;]*m")]
	private static partial Regex AnsiEscape();

	private static ConnectionServerService.ConnectionData Connection(
		OutputFormat format = OutputFormat.Ansi,
		string connectionType = "telnet",
		string? mxpSupported = null) =>
		new(
			Handle: 1,
			PlayerDbRef: null,
			State: ConnectionServerService.ConnectionState.Connected,
			OutputFunction: _ => ValueTask.CompletedTask,
			PromptOutputFunction: _ => ValueTask.CompletedTask,
			EncodingFunction: () => Encoding.UTF8,
			DisconnectFunction: () => { },
			GMCPFunction: null,
			Capabilities: new ProtocolCapabilities(Format: format, MxpSupported: mxpSupported),
			Preferences: null,
			ConnectionType: connectionType);

	// ── MXP: what the client said it can render ─────────────────────────────────

	/// <summary>
	/// MXP asks with <c>&lt;SUPPORT&gt;</c> for a reason: a client that cannot show a picture is better
	/// off without the tag, and one that cannot open a frame is better off with the text that would have
	/// gone in it.
	/// </summary>
	[Test]
	public async Task Mxp_WritesOnlyWhatTheClientAnsweredFor()
	{
		var line = MarkupText.Concat([
			MarkupText.Sound("door.wav"),
			MarkupText.Image("map.png", "A map"),
			MarkupText.Plain(" It creaks.")]);
		var markup = MarkupTextSerializer.Serialize(line);

		var rendered = Encoding.UTF8.GetString(
			new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp, mxpSupported: "SOUND")).Data);

		await Assert.That(rendered).Contains("<SOUND door.wav>");
		await Assert.That(rendered).DoesNotContain("<IMAGE");
		await Assert.That(rendered).Contains("A map")
			.Because("a picture the client cannot show still leaves its description");
		await Assert.That(rendered).Contains("It creaks.");
	}

	[Test]
	public async Task Mxp_KeepsThePaneTextWhenTheClientCannotOpenOne()
	{
		var markup = MarkupTextSerializer.Serialize(
			MarkupText.Pane(MarkupText.Plain("North: the gate"), "map"));

		var rendered = Encoding.UTF8.GetString(
			new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp, mxpSupported: "SOUND")).Data);

		await Assert.That(rendered).DoesNotContain("<FRAME");
		await Assert.That(rendered).Contains("North: the gate")
			.Because("a frame the client cannot open would otherwise take its text somewhere the player never sees");
	}

	/// <summary>A client that answered nothing gets nothing it did not answer for.</summary>
	[Test]
	public async Task Mxp_AnAnswerOfNothingGatesEverything()
	{
		var markup = MarkupTextSerializer.Serialize(
			MarkupText.Concat([MarkupText.Sound("door.wav"), MarkupText.Plain("It creaks.")]));

		var rendered = Encoding.UTF8.GetString(
			new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp, mxpSupported: "")).Data);

		await Assert.That(rendered).DoesNotContain("<SOUND");
		await Assert.That(rendered).Contains("It creaks.");
	}

	/// <summary>
	/// A connection that was never asked is not a connection that refused — an older one, or one
	/// negotiated before the question was put.
	/// </summary>
	[Test]
	public async Task Mxp_WithNoAnswerAtAllEverythingIsWritten()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Sound("door.wav"));

		var rendered = Encoding.UTF8.GetString(
			new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp)).Data);

		await Assert.That(rendered).Contains("<SOUND door.wav>");
	}

	[Test]
	public async Task Mxp_SecureLinesStillFrameTheOutput()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain("Hello"));

		var rendered = Encoding.UTF8.GetString(
			new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp, mxpSupported: "SOUND")).Data);

		await Assert.That(rendered).StartsWith("\u001b[1z")
			.Because("a gated registry is still the wire registry, and a tag is only read on a secure line");
	}

	/// <summary>
	/// A Pueblo client renders the stream as HTML, where the CRLF the telnet layer puts after each
	/// message is whitespace — so without a break of its own every message runs into the next. This is
	/// PennMUSH's <c>queue_eol</c> in HTML mode.
	/// </summary>
	[Test]
	public async Task Pueblo_EndsAMessageWithABreak()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain("You see nothing special."));

		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Pueblo));

		await Assert.That(Encoding.UTF8.GetString(result.Data)).IsEqualTo("You see nothing special.<BR>");
	}

	[Test]
	public async Task Pueblo_DoesNotEndAPrompt()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain("Password:"));

		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Pueblo), prompt: true);

		await Assert.That(Encoding.UTF8.GetString(result.Data)).IsEqualTo("Password:")
			.Because("what follows a prompt is the player's own typing, on the same line");
	}

	[Test]
	public async Task Pueblo_DoesNotDoubleABreakTheTextAlreadyEndsWith()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain("Look out!\n"));

		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Pueblo));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(text.EndsWith("<BR>", StringComparison.Ordinal)).IsTrue();
		await Assert.That(text.EndsWith("<BR><BR>", StringComparison.Ordinal)).IsFalse();
	}

	[Test]
	[Arguments(OutputFormat.Ansi)]
	[Arguments(OutputFormat.Mxp)]
	public async Task EveryOtherFormatEndsAMessageWithTheWiresOwnEnding(OutputFormat format)
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain("You see nothing special."));

		var result = new MarkupOutputRenderer().Render(markup, Connection(format));

		await Assert.That(Encoding.UTF8.GetString(result.Data)).DoesNotContain("<BR>")
			.Because("a terminal and an MXP client both read the CRLF the telnet layer writes");
	}

	[Test]
	public async Task Pueblo_HtmlEncodesPlainText()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Pueblo));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		// MarkupString 2.0 encodes only the characters that are markup in HTML text — < > & — and
		// leaves quotes alone. A quote is markup inside an attribute value, and this encoding is
		// never applied to one.
		await Assert.That(text).Contains("&lt;send href=\"look\"&gt;Tom &amp; \"Sue\"&lt;/send&gt;");
	}

	[Test]
	public async Task Mxp_PrefixesLinesAndHtmlEncodes()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		await Assert.That(text).Contains(
			$"{MxpSecureLineFramer.SecureLine}&lt;send href=\"look\"&gt;Tom &amp; \"Sue\"&lt;/send&gt;");
	}

	[Test]
	public async Task Mxp_HelpLinksUseSecureModeOnEveryLine()
	{
		var link = SharpMUSH.Documentation.MarkdownToAsciiRenderer.RecursiveMarkdownHelper.RenderMarkdown("[newbie]");
		var markup = MarkupTextSerializer.Serialize(MarkupText.Concat(MarkupText.Concat(link, MarkupText.Plain("\n")), link));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp));
		var lines = Encoding.UTF8.GetString(result.Data).Split("\r\n");

		await Assert.That(lines.Length).IsEqualTo(2);
		foreach (var line in lines)
		{
			await Assert.That(line.StartsWith(MxpSecureLineFramer.SecureLine)).IsTrue();
			await Assert.That(line).Contains("<SEND HREF=\"help newbie\">help newbie</SEND>");
		}
	}

	/// <summary>
	/// MXP line modes share CSI syntax with SGR, so a connection rendering no colour still needs them:
	/// without <c>ESC[1z</c> the client treats the SEND tag as text. Both ways of arriving at no colour are
	/// covered — a client that negotiated none, which renders attributes only, and a player who pinned
	/// <c>SOCKSET colorstyle plain</c>.
	/// </summary>
	[Test]
	[Arguments(true, ColorStyles.Plain)]
	[Arguments(false, null)]
	public async Task Mxp_ModeSurvivesDisablingAnsi(bool supportsAnsi, string? pin)
	{
		var link = AnsiMarkup.Create(linkUrl: "help newbie", linkKind: LinkKind.Command);
		var text = MarkupText.Wrap(link, MarkupText.Wrap(Red, "newbie"));

		var rendered = Render(text, new ProtocolCapabilities(SupportsAnsi: supportsAnsi, Format: OutputFormat.Mxp, ColorStylePin: pin),
			new PlayerOutputPreferences(AnsiEnabled: false, ColorEnabled: false));

		await Assert.That(rendered).IsEqualTo($"{MxpSecureLineFramer.SecureLine}<SEND HREF=\"help newbie\">newbie</SEND>");
	}

	[Test]
	public async Task Ansi_KeepsRawText()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(result.ApplyOutputTransform).IsTrue();
		await Assert.That(text).Contains(Raw);
	}

	/// <summary>
	/// An HtmlMarkup span (what <c>tagwrap()</c> returns) must not reach a client that negotiated no
	/// Pueblo/MXP: a plain telnet client has no idea what &lt;send&gt; is and prints the tag literally.
	/// Rendering an ANSI connection natively rather than as ANSI leaked the tags.
	/// </summary>
	[Test]
	public async Task Ansi_StripsHtmlMarkupTags()
	{
		var send = HtmlMarkup.Create("send", "href=\"north\" hint=\"Go north\"");
		var markup = MarkupTextSerializer.Serialize(MarkupText.Wrap(send, MarkupText.Plain("north")));

		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(text).DoesNotContain("<send");
		await Assert.That(text).DoesNotContain("</send>");
		// The trailing ANSI reset the ANSI strategy appends to any markup-bearing string is expected.
		await Assert.That(StripAnsi(text)).IsEqualTo("north");
	}

	/// <summary>
	/// An unmarked label followed by a tag-wrapped name. The leading plain run means the first markup
	/// in the string is the HtmlMarkup, which is what selected the native (tag-emitting) render
	/// strategy.
	/// </summary>
	[Test]
	public async Task Ansi_StripsHtmlMarkupTagsInAMixedLine()
	{
		var send = HtmlMarkup.Create("send", "href=\"north\" hint=\"Go north\"");
		var line = MarkupText.Concat(
			MarkupText.Plain("Obvious exits:\n"),
			MarkupText.Wrap(send, MarkupText.Plain("north")));

		var result = new MarkupOutputRenderer().Render(MarkupTextSerializer.Serialize(line), Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(text).DoesNotContain("<send");
		await Assert.That(StripAnsi(text)).IsEqualTo("Obvious exits:\r\nnorth");
	}

	/// <summary>
	/// Colour must survive the ANSI render — the fix must not turn styled output into plain text.
	/// </summary>
	[Test]
	public async Task Ansi_KeepsAnsiMarkupAlongsideStrippedHtml()
	{
		var send = HtmlMarkup.Create("send", "href=\"north\"");
		var red = AnsiMarkup.Create(foreground: Color.Red.ToAnsiColor());
		var line = MarkupText.Wrap(MarkupSet.Of([red, send]), "north");

		var result = new MarkupOutputRenderer().Render(MarkupTextSerializer.Serialize(line), Connection(OutputFormat.Ansi));
		var text = Encoding.UTF8.GetString(result.Data);

		await Assert.That(text).DoesNotContain("<send");
		await Assert.That(text).Contains("\x1b[");
		await Assert.That(text).Contains("north");
	}

	[Test]
	public async Task WebSocket_WrapsMarkupEnvelopeWithoutTransform()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(Raw));
		var result = new MarkupOutputRenderer().Render(markup, Connection(connectionType: "websocket"));
		var text = Encoding.UTF8.GetString(result.Data);

		// The envelope is JSON the browser renders itself, so the ANSI/charset transform must be skipped.
		await Assert.That(result.ApplyOutputTransform).IsFalse();

		using var doc = JsonDocument.Parse(text);
		var root = doc.RootElement;
		await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("markup");

		var data = root.GetProperty("data").GetString()!;
		await Assert.That(MarkupTextSerializer.Deserialize(data).ToPlainText()).IsEqualTo(Raw);
	}

	/// <summary>
	/// Every line break goes out as CRLF exactly once: a bare LF grows a CR, an existing CRLF is not
	/// doubled, a CR that precedes anything but LF is text, and trailing breaks are trimmed away.
	/// </summary>
	[Test]
	[Arguments("one\ntwo", "one\r\ntwo")]
	[Arguments("one\r\ntwo", "one\r\ntwo")]
	[Arguments("one\r\r\ntwo", "one\r\r\ntwo")]
	[Arguments("one\n\rtwo", "one\r\n\rtwo")]
	[Arguments("one\rtwo", "one\rtwo")]
	[Arguments("one\n\n", "one")]
	[Arguments("one\r\n\r\n", "one")]
	[Arguments("\n\none", "\r\n\r\none")]
	[Arguments("one\n\ntwo", "one\r\n\r\ntwo")]
	[Arguments("one\r\n\r\ntwo", "one\r\n\r\ntwo")]
	[Arguments("\r\n", "")]
	public async Task Terminal_NormalisesLineEndings(string input, string expected)
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain(input));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Ansi));

		await Assert.That(Encoding.UTF8.GetString(result.Data)).IsEqualTo(expected);
	}

	/// <summary>
	/// MXP secure mode opens every line that has content, and only those: a blank line, or one that is
	/// nothing but the CR of a CRLF pair, gets no prefix, and the line breaks themselves are untouched.
	/// </summary>
	[Test]
	public async Task Mxp_PrefixesOnlyLinesWithContent()
	{
		var markup = MarkupTextSerializer.Serialize(MarkupText.Plain("one\n\ntwo\r\nthree"));
		var result = new MarkupOutputRenderer().Render(markup, Connection(OutputFormat.Mxp));

		var prefix = MxpSecureLineFramer.SecureLine;
		await Assert.That(Encoding.UTF8.GetString(result.Data))
			.IsEqualTo($"{prefix}one\r\n\r\n{prefix}two\r\n{prefix}three");
	}

	// ── Colour: what the connection can display ─────────────────────────────────

	private static readonly AnsiMarkup Red = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false));
	private static readonly AnsiMarkup BoldRed = AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false), bold: true);
	private static readonly AnsiMarkup RgbRed = AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0));
	private static readonly AnsiMarkup XtermRed = AnsiMarkup.Create(foreground: new AnsiColor.Xterm(196));

	private static readonly PlayerOutputPreferences AllColour = new(
		AnsiEnabled: true, ColorEnabled: true, Xterm256Enabled: true, TruecolorEnabled: true);

	private static string Render(MarkupText text, ProtocolCapabilities capabilities, PlayerOutputPreferences? preferences) =>
		Encoding.UTF8.GetString(new MarkupOutputRenderer().Render(MarkupTextSerializer.Serialize(text),
			new RenderContext("telnet", capabilities, preferences)).Data);

	/// <summary>
	/// The bug this ladder exists to prevent. Logging in publishes the character's colour flags, and a
	/// character with neither ANSI nor COLOR published <c>false</c> for both, which read as "this player
	/// refuses colour". The default <c>player_flags</c> grants <c>ansi</c> and never <c>color</c>, so no
	/// character could get colour without setting the flags by hand. An unset flag is not a refusal.
	/// </summary>
	[Test]
	public async Task Colour_IsKept_WhenFlagsAreUnsetButTerminalClaimsAnsi()
	{
		var rendered = Render(MarkupText.Wrap(Red, "Red text"), new ProtocolCapabilities(SupportsAnsi: true),
			new PlayerOutputPreferences(AnsiEnabled: false, ColorEnabled: false, Xterm256Enabled: false, TruecolorEnabled: false));

		await Assert.That(rendered).IsEqualTo("\x1b[31mRed text\x1b[0m");
	}

	/// <summary>The same fact one rung up: a terminal that claims 24-bit colour gets it, flags or no flags.</summary>
	[Test]
	public async Task Colour_TruecolorIsKept_WhenFlagIsUnsetButTerminalClaimsIt()
	{
		var rendered = Render(MarkupText.Wrap(RgbRed, "Red text"),
			new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: true),
			new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: true));

		await Assert.That(rendered).IsEqualTo("\x1b[38;2;255;0;0mRed text\x1b[0m");
	}

	/// <summary>
	/// Refusing colour is <c>SOCKSET colorstyle</c>'s job, not a flag's: only a pin renders below what the
	/// client and the flags between them claim.
	/// </summary>
	[Test]
	public async Task Colour_PinnedPlain_WritesNoSgr()
	{
		var text = MarkupText.Concat([MarkupText.Wrap(BoldRed, "Bold Red"), MarkupText.Plain(" "),
			MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(2, false), underlined: true), "Underline Green")]);

		var rendered = Render(text, new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: true, ColorStylePin: ColorStyles.Plain), AllColour);

		await Assert.That(rendered).IsEqualTo("Bold Red Underline Green");
	}

	/// <summary>PennMUSH's middle rung: the attributes survive and the hues do not, extended colours included.</summary>
	[Test]
	public async Task Colour_PinnedHilite_KeepsAttributesAndDropsColour()
	{
		var text = MarkupText.Concat([
			MarkupText.Wrap(BoldRed, "Bold red"), MarkupText.Plain(" "),
			MarkupText.Wrap(AnsiMarkup.Create(underlined: true), "Underline"), MarkupText.Plain(" "),
			MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Xterm(196), bold: true), "Xterm"), MarkupText.Plain(" "),
			MarkupText.Wrap(AnsiMarkup.Create(background: new AnsiColor.Xterm(21)), "Background")]);

		var rendered = Render(text, new ProtocolCapabilities(SupportsAnsi: true, ColorStylePin: ColorStyles.Hilite), null);

		await Assert.That(rendered).IsEqualTo(
			"\x1b[1mBold red\x1b[0m \x1b[4mUnderline\x1b[0m \x1b[1mXterm\x1b[0m Background");
	}

	/// <summary>A pin renders below the terminal's own claim, which is the whole point of pinning one.</summary>
	[Test]
	public async Task Colour_PinnedSixteenColor_DowngradesATruecolorTerminal()
	{
		var rendered = Render(MarkupText.Wrap(RgbRed, "Red text"),
			new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: true,
				ColorStylePin: ColorStyles.SixteenColor), AllColour);

		await Assert.That(rendered).IsEqualTo("\x1b[31mRed text\x1b[0m");
	}

	/// <summary>
	/// A pin is the player speaking for themselves, so it also overrides the screen-reader default — somebody
	/// running a screen reader alongside a colour-capable terminal can ask for the colour.
	/// </summary>
	[Test]
	public async Task Colour_PinOverridesScreenReaderDefault()
	{
		var rendered = Render(MarkupText.Wrap(Red, "Red text"),
			new ProtocolCapabilities(SupportsAnsi: false, ScreenReader: true, ColorStylePin: ColorStyles.SixteenColor), null);

		await Assert.That(rendered).IsEqualTo("\x1b[31mRed text\x1b[0m");
	}

	[Test]
	public async Task Colour_ScreenReaderOverridesPlayerColorFlags()
	{
		var rendered = Render(MarkupText.Wrap(BoldRed, "Red text"), new ProtocolCapabilities(SupportsAnsi: false, ScreenReader: true), AllColour);

		await Assert.That(rendered).IsEqualTo("Red text");
	}

	/// <summary>
	/// sharpflag.md's split, honoured: ANSI is "this client can highlight", COLOR is "this client can colour".
	/// A player with only the first, on a terminal claiming nothing, gets the attributes; so does a terminal
	/// that named no colour before anyone logged in.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task Colour_NoColourClaimed_RendersHilite(bool ansiFlag)
	{
		var text = MarkupText.Concat([MarkupText.Wrap(BoldRed, "Bold red"), MarkupText.Plain(" "), MarkupText.Wrap(Red, "red")]);

		var rendered = Render(text, new ProtocolCapabilities(SupportsAnsi: false),
			ansiFlag ? new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: false) : null);

		await Assert.That(rendered).IsEqualTo("\x1b[1mBold red\x1b[0m red");
	}

	/// <summary>A flag that says yes is a claim like a terminal's, and the deeper claim wins.</summary>
	[Test]
	[Arguments(true, false, false, "\x1b[31mRed text\x1b[0m")]
	[Arguments(true, true, false, "\x1b[38;5;196mRed text\x1b[0m")]
	[Arguments(true, false, true, "\x1b[38;2;255;0;0mRed text\x1b[0m")]
	public async Task Colour_PlayerFlagsOverrideInferredCapability(bool color, bool xterm256, bool truecolor, string expected)
	{
		var rendered = Render(MarkupText.Wrap(RgbRed, "Red text"),
			new ProtocolCapabilities(SupportsAnsi: false, SupportsXterm256: false, SupportsTruecolor: false),
			new PlayerOutputPreferences(AnsiEnabled: true, ColorEnabled: color, Xterm256Enabled: xterm256, TruecolorEnabled: truecolor));

		await Assert.That(rendered).IsEqualTo(expected);
	}

	[Test]
	public async Task Colour_UsesInferredTruecolorCapabilityBeforeLogin()
	{
		var rendered = Render(MarkupText.Wrap(RgbRed, "Red text"),
			new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: true, SupportsTruecolor: true), null);

		await Assert.That(rendered).IsEqualTo("\x1b[38;2;255;0;0mRed text\x1b[0m");
	}

	/// <summary>
	/// Colour depth is a ladder: 24-bit becomes the nearest palette entry for a 256-colour client, and a palette
	/// entry or 24-bit colour becomes the nearest of the sixteen for one that has only those. Background as
	/// well as foreground, and a colour sharing its sequence with attributes as well as one alone.
	/// </summary>
	[Test]
	[Arguments(true, "\x1b[1;4;38;5;196mText\x1b[0m \x1b[48;5;21mOn blue\x1b[0m \x1b[38;5;196mXterm\x1b[0m")]
	[Arguments(false, "\x1b[1;4;31mText\x1b[0m \x1b[44mOn blue\x1b[0m \x1b[31mXterm\x1b[0m")]
	public async Task Colour_DowngradesToWhatTheTerminalClaims(bool xterm256, string expected)
	{
		var text = MarkupText.Concat([
			MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Rgb(255, 0, 0), bold: true, underlined: true), "Text"),
			MarkupText.Plain(" "),
			MarkupText.Wrap(AnsiMarkup.Create(background: new AnsiColor.Rgb(0, 0, 255)), "On blue"),
			MarkupText.Plain(" "),
			MarkupText.Wrap(XtermRed, "Xterm")]);

		var rendered = Render(text, new ProtocolCapabilities(SupportsAnsi: true, SupportsXterm256: xterm256, SupportsTruecolor: false), null);

		await Assert.That(rendered).IsEqualTo(expected);
	}

	/// <summary>
	/// The first sixteen palette entries are the sixteen colours themselves; the bright half is written as
	/// bold and the base colour (PennMUSH's own form, which every client reads), and a bright background on
	/// the aixterm row.
	/// </summary>
	[Test]
	[Arguments(9, false, "\x1b[1;31mText\x1b[0m")]
	[Arguments(1, false, "\x1b[31mText\x1b[0m")]
	[Arguments(9, true, "\x1b[101mText\x1b[0m")]
	public async Task Colour_TheFirstSixteenPaletteEntriesAreTheSixteenColours(int index, bool background, string expected)
	{
		var colour = new AnsiColor.Xterm((byte)index);
		var style = background ? AnsiMarkup.Create(background: colour) : AnsiMarkup.Create(foreground: colour);

		var rendered = Render(MarkupText.Wrap(style, "Text"), new ProtocolCapabilities(SupportsAnsi: true), null);

		await Assert.That(rendered).IsEqualTo(expected);
	}

	/// <summary>Telnet clients do not read OSC 8: a link is its text, at every depth.</summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task Colour_ALinkIsItsText(bool supportsAnsi)
	{
		var link = AnsiMarkup.Create(linkUrl: "https://example.com/help", linkKind: LinkKind.Url);
		var text = MarkupText.Concat([MarkupText.Plain("See "), MarkupText.Wrap(link, "newbie2"), MarkupText.Plain(" for more.")]);

		var rendered = Render(text, new ProtocolCapabilities(SupportsAnsi: supportsAnsi), AllColour);

		await Assert.That(rendered).IsEqualTo("See newbie2 for more.");
	}

	// ── Layouts: laid out again for the client ──────────────────────────────────

	private static string Box(bool fluid) => MarkupTextSerializer.Serialize(BlockLayout.Build(
		new BoxNode(new TextNode(MarkupText.Plain("Hi")), BorderStyle.Single, MarkupText.Plain("T")), 12, fluid));

	private static string RenderFor(string markup, ProtocolCapabilities capabilities) =>
		StripAnsi(Encoding.UTF8.GetString(new MarkupOutputRenderer().Render(markup, Connection() with { Capabilities = capabilities }).Data))
			.Replace("\r\n", "\n");

	/// <summary>A box drawn at the width of the connection that ran the command is sent to each reader at theirs.</summary>
	[Test]
	public async Task AFluidLayout_IsLaidOutAtTheClientsWidth()
	{
		var rendered = RenderFor(Box(fluid: true), new ProtocolCapabilities(Width: 20));

		await Assert.That(rendered.TrimEnd('\n')).IsEqualTo(
			"┌───────┤ T ├──────┐\n│ Hi               │\n└──────────────────┘");
	}

	[Test]
	public async Task AFixedLayout_KeepsItsWidth()
	{
		var rendered = RenderFor(Box(fluid: false), new ProtocolCapabilities(Width: 20));

		await Assert.That(rendered.Split('\n')[0].Length).IsEqualTo(12);
	}

	[Test]
	public async Task AClientWithoutUtf8_GetsAsciiBorders()
	{
		var rendered = RenderFor(Box(fluid: false), new ProtocolCapabilities(SupportsUtf8: false));

		await Assert.That(rendered).DoesNotContain("─");
		await Assert.That(rendered).Contains("Hi");
	}

	[Test]
	public async Task AScreenReader_GetsTheContentWithoutBorders()
	{
		var rendered = RenderFor(Box(fluid: false), new ProtocolCapabilities(ScreenReader: true));

		await Assert.That(rendered).DoesNotContain("─");
		await Assert.That(rendered).DoesNotContain("│");
		await Assert.That(rendered).Contains("Hi");
	}
}
