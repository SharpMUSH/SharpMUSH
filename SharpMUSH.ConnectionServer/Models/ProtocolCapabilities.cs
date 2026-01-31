namespace SharpMUSH.ConnectionServer.Models;

/// <summary>
/// Represents the protocol capabilities supported by a client connection
/// </summary>
/// <param name="SupportsAnsi">Whether the client supports basic 16-color ANSI codes</param>
/// <param name="SupportsXterm256">Whether the client supports 256-color ANSI codes</param>
/// <param name="SupportsUtf8">Whether the client supports UTF-8 encoding</param>
/// <param name="Charset">The character set used by the client (e.g., "UTF-8", "ASCII", "LATIN-1")</param>
/// <param name="MaxLineLength">Maximum line length supported by the client (-1 = unlimited)</param>
public record ProtocolCapabilities(
	bool SupportsAnsi = true,
	bool SupportsXterm256 = false,
	bool SupportsUtf8 = true,
	string Charset = "UTF-8",
	int MaxLineLength = -1
);
