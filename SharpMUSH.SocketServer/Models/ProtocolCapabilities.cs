namespace SharpMUSH.ConnectionServer.Models;

/// <summary>
/// Output format for a connection — determines how MarkupString content is rendered.
/// </summary>
public enum OutputFormat
{
	/// <summary>Standard ANSI escape codes (default)</summary>
	Ansi,
	/// <summary>Pueblo HTML subset (B, I, FONT, A, SEND tags)</summary>
	Pueblo,
	/// <summary>Full MXP with line security modes</summary>
	Mxp
}

/// <summary>
/// Represents the protocol capabilities supported by a client connection
/// </summary>
/// <param name="SupportsAnsi">Whether the client supports basic 16-color ANSI codes</param>
/// <param name="SupportsXterm256">Whether the client supports 256-color ANSI codes (ESC[38;5;n)</param>
/// <param name="SupportsTruecolor">Whether the client supports 24-bit RGB ANSI codes (ESC[38;2;r;g;b)</param>
/// <param name="SupportsUtf8">Whether the client supports UTF-8 encoding</param>
/// <param name="Charset">The character set used by the client (e.g., "UTF-8", "ASCII", "LATIN-1")</param>
/// <param name="MaxLineLength">Maximum line length supported by the client (-1 = unlimited)</param>
/// <param name="Format">The output format negotiated for this connection</param>
/// <param name="ScreenReader">Whether MTTS identified the client as a screen reader</param>
/// <param name="ColorStylePin">
/// A colour style the player pinned with <c>SOCKSET colorstyle</c> — one of the
/// <see cref="SharpMUSH.Library.Utilities.ColorStyles"/> values. Null means nothing is pinned and the
/// depth is worked out from the negotiated capabilities and the player's flags. This is the only way
/// to render <i>below</i> what the client and the flags between them claim, so it is what turns
/// colour off for a player who does not want it.
/// </param>
public record ProtocolCapabilities(
	bool SupportsAnsi = true,
	bool SupportsXterm256 = false,
	bool SupportsTruecolor = false,
	bool SupportsUtf8 = true,
	string Charset = "UTF-8",
	int MaxLineLength = -1,
	OutputFormat Format = OutputFormat.Ansi,
	bool ScreenReader = false,
	string? ColorStylePin = null
);
