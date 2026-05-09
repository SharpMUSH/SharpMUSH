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
	/// </summary>
	public bool PuebloEnabled { get; set; } = true;

	/// <summary>
	/// Enable MXP (MUD eXtension Protocol) telnet negotiation.
	/// When true, the server offers MXP via telnet option 91.
	/// Can be enabled independently of Pueblo.
	/// </summary>
	public bool MxpEnabled { get; set; } = true;
}
