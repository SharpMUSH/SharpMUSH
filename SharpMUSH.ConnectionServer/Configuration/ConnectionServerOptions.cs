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
}
