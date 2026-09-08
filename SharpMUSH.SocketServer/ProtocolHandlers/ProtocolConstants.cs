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

	/// <summary>Open line — only "safe" tags (B, I, U, COLOR, FONT). For user-generated content.</summary>
	public const string MxpLineOpen = "\x1b[0z";
	/// <summary>Secure line — allows SEND, A, IMG, SOUND. For server-generated content.</summary>
	public const string MxpLineSecure = "\x1b[1z";
	/// <summary>Locked line — no tag interpretation at all.</summary>
	public const string MxpLineLocked = "\x1b[2z";
}
