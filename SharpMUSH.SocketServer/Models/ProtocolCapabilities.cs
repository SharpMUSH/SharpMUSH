using System.Text.Json.Serialization;
using MarkupString.Ansi;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.SocketServer.Models;

/// <summary>
/// Output format for a connection — determines how MarkupString content is rendered.
/// </summary>
public enum OutputFormat
{
	/// <summary>Standard ANSI escape codes (default)</summary>
	Ansi,
	/// <summary>
	/// Pueblo: an HTML subset, with command links written <c>&lt;A XCH_CMD&gt;</c>. Pueblo has no
	/// <c>&lt;SEND&gt;</c>.
	/// </summary>
	Pueblo,
	/// <summary>
	/// MXP: its own tag set under line security modes, with command links written
	/// <c>&lt;SEND HREF&gt;</c>. MXP has no <c>XCH_CMD</c>; it is a different dialect from Pueblo, not an
	/// extension of it.
	/// </summary>
	Mxp
}

public static class OutputFormatNegotiation
{
	/// <summary>
	/// The format a connection renders in after a client offers <paramref name="offered"/> on top of
	/// <paramref name="current"/>. A client that answers both the MXP telnet option and the Pueblo
	/// handshake keeps MXP whichever arrives first — the engine's negotiation consumers apply the same
	/// rule, so the renderer and <c>terminfo()</c> cannot disagree about which dialect is on the wire.
	/// </summary>
	public static OutputFormat Negotiate(this OutputFormat current, OutputFormat offered)
		=> current == OutputFormat.Mxp && offered == OutputFormat.Pueblo ? OutputFormat.Mxp : offered;
}

/// <summary>
/// Represents the protocol capabilities supported by a client connection
/// </summary>
/// <param name="SupportsAnsi">Whether the client supports basic 16-color ANSI codes</param>
/// <param name="SupportsXterm256">Whether the client supports 256-color ANSI codes (ESC[38;5;n)</param>
/// <param name="SupportsTruecolor">Whether the client supports 24-bit RGB ANSI codes (ESC[38;2;r;g;b)</param>
/// <param name="SupportsUtf8">Whether the client claims UTF-8 (MTTS), or has not reported its terminal yet</param>
/// <param name="Charset">
/// The character set telnet CHARSET negotiation settled on (one of <see cref="TerminalCharsets"/>), or null when it
/// never did. What the connection is written in is <see cref="OutputCharset"/>.
/// </param>
/// <param name="MaxLineLength">Maximum line length supported by the client (-1 = unlimited)</param>
/// <param name="Format">The output format negotiated for this connection</param>
/// <param name="ScreenReader">
/// Whether MTTS identified the client as a screen reader. What output goes by is <see cref="ReadsAloud"/>,
/// which a <c>SCREENREADER</c> pin overrides.
/// </param>
/// <param name="ColorStylePin">
/// A colour style the player pinned with <c>SOCKSET colorstyle</c> — one of the
/// <see cref="SharpMUSH.Library.Utilities.ColorStyles"/> values. Null means nothing is pinned and the
/// depth is worked out from the negotiated capabilities and the player's flags. This is the only way
/// to render <i>below</i> what the client and the flags between them claim, so it is what turns
/// colour off for a player who does not want it.
/// </param>
/// <param name="Width">
/// The width the client last reported through NAWS, or zero when it never has. A layout drawn by
/// <c>box()</c>, <c>flex()</c> and the like at an automatic width is laid out again at this width
/// before it is sent.
/// </param>
/// <param name="TerminalTypes">The terminal types the client reported, which name its terminal.</param>
/// <param name="Probe">What the terminal answered when asked, or null when it never was.</param>
/// <param name="Pins">What the player set with <c>SOCKSET</c>; null for nothing.</param>
/// <param name="CellWidth">The terminal's character cell width in pixels, or zero when it never said.</param>
/// <param name="CellHeight">The terminal's character cell height in pixels, or zero when it never said.</param>
public record ProtocolCapabilities(
	bool SupportsAnsi = true,
	bool SupportsXterm256 = false,
	bool SupportsTruecolor = false,
	bool SupportsUtf8 = true,
	string? Charset = null,
	int MaxLineLength = -1,
	OutputFormat Format = OutputFormat.Ansi,
	bool ScreenReader = false,
	string? ColorStylePin = null,
	string? MxpSupported = null,
	int Width = 0,
	IReadOnlyList<string>? TerminalTypes = null,
	TerminalProbeResult? Probe = null,
	TerminalPins? Pins = null,
	int CellWidth = 0,
	int CellHeight = 0
)
{
	/// <summary>
	/// The character set output is written in, through <see cref="TerminalCharsets.Resolve"/>. A terminal SharpMUSH
	/// knows (<see cref="Terminal"/>) shows UTF-8 whether or not it says so; most never do.
	/// </summary>
	[JsonIgnore]
	public string OutputCharset => TerminalCharsets.Resolve(Pins, Charset, SupportsUtf8 || Terminal is not null);

	/// <summary>Whether output is written in UTF-8; otherwise each character the client cannot show is replaced.</summary>
	[JsonIgnore]
	public bool Utf8 => OutputCharset == TerminalCharsets.Utf8;

	/// <summary>The terminal, through <see cref="TerminalFeatureReader.Identify"/>, or null when it is not one this server knows.</summary>
	[JsonIgnore]
	public TerminalProfile? Terminal => TerminalFeatureReader.Identify(TerminalTypes ?? [], Probe, Pins?.Terminal);

	/// <summary>
	/// What this connection is sent beyond colour: what the terminal can do under the pins, through
	/// <see cref="TerminalFeatureReader.Resolve"/>, the calculation <c>terminfo()</c> also reports.
	/// </summary>
	[JsonIgnore]
	public TerminalFeatures Features
	{
		get
		{
			var terminal = Terminal;
			return TerminalFeatureReader.Resolve(TerminalFeatureReader.Detect(terminal, TerminalTypes ?? [], Probe), terminal,
				Pins ?? TerminalPins.None, Utf8,
				ReadsAloud);
		}
	}

	/// <summary>
	/// Whether a screen reader reads this connection: the player's <c>SCREENREADER</c> pin, or else what MTTS
	/// said, the way <see cref="TerminalCapabilityReader.Read(IReadOnlyDictionary{string, string})"/> reads it
	/// for <c>terminfo()</c>.
	/// </summary>
	[JsonIgnore]
	public bool ReadsAloud => Pins?.ScreenReader ?? ScreenReader;
}
