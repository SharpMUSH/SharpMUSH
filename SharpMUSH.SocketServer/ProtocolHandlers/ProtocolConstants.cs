namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Wire-protocol constants for the optional Pueblo and MXP client protocols. These lived in
/// SharpMUSH.Library's ErrorMessages, but they are protocol strings only the connection server
/// speaks — relocated as part of decoupling this process from the full Library.
/// </summary>
public static class ProtocolConstants
{
	// --- Pueblo protocol (PennMUSH hdrs/conf.h) ---
	public const string PuebloHello = "This world is Pueblo 1.10 Enhanced.\r\n";

	/// <summary>
	/// PennMUSH <c>PUEBLO_SEND</c>: the answer to <c>PUEBLOCLIENT</c>, which moves the client out of text
	/// mode into HTML. A client never sent it shows every tag as text.
	/// </summary>
	public const string PuebloStart = "</xch_mudtext><img xch_mode=purehtml><xch_page clear=text>\n";

	/// <summary>
	/// PennMUSH <c>PUEBLO_SEND_SHORT</c>: <see cref="PuebloStart"/> without the clear, for a repeated
	/// <c>PUEBLOCLIENT</c> from a client that thinks it is still showing raw HTML.
	/// </summary>
	public const string PuebloRestart = "</xch_mudtext><img xch_mode=purehtml>\n";

	/// <summary>Secure line — allows SEND, A, IMG, SOUND. For server-generated content.</summary>
	public const string MxpLineSecure = "\x1b[1z";
}
