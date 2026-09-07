namespace SharpMUSH.ConnectionServer.Configuration;

/// <summary>
/// Configuration settings for the ConnectionServer
/// </summary>
public class ConnectionServerOptions
{
	/// <summary>
	/// Port for Telnet connections
	/// </summary>
	public int TelnetPort { get; set; } = 4201;

	/// <summary>
	/// Port for HTTP/WebSocket connections
	/// </summary>
	public int HttpPort { get; set; } = 4202;

	/// <summary>
	/// Starting descriptor number for Telnet connections
	/// </summary>
	public long TelnetDescriptorStart { get; set; } = 0;

	/// <summary>
	/// Starting descriptor number for WebSocket connections
	/// </summary>
	public long WebSocketDescriptorStart { get; set; } = 1000000;

	/// <summary>
	/// Enable Pueblo protocol handshake on telnet connections.
	/// When true, the server sends the Pueblo hello string on connect
	/// and listens for PUEBLOCLIENT responses.
	/// Defaults to false; override via appsettings.json "ConnectionServer:PuebloEnabled".
	/// The main server's NetOptions.Pueblo (mushcnf) is a separate toggle for
	/// Pueblo feature handling at the application layer.
	/// </summary>
	public bool PuebloEnabled { get; set; } = false;

	/// <summary>
	/// Enable MXP (MUD eXtension Protocol) telnet negotiation.
	/// When true, the server offers MXP via telnet option 91.
	/// Can be enabled independently of Pueblo.
	/// <para>
	/// Defaults to true, unlike <see cref="PuebloEnabled"/>: MXP is a negotiated telnet option, so a
	/// client that does not want it answers IAC DONT and sees nothing, whereas the Pueblo handshake
	/// is unsolicited plain text that a non-Pueblo client renders as junk on its first screen.
	/// Leaving it off meant the MXP plugin was never registered at all, so the server never sent
	/// IAC WILL MXP and no client — however capable — could negotiate it.
	/// </para>
	/// Override via appsettings.json "ConnectionServer:MxpEnabled".
	/// The main server's NetOptions.Mxp (mushcnf) is a separate toggle for
	/// MXP feature handling at the application layer.
	/// </summary>
	public bool MxpEnabled { get; set; } = true;
}
