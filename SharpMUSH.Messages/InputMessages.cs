namespace SharpMUSH.Messages;

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a player submits input
/// </summary>
public record TelnetInputMessage(long Handle, string Input);

/// <summary>
/// Message sent from ConnectionServer to MainProcess for GMCP signals
/// </summary>
public record GMCPSignalMessage(long Handle, string Package, string Info);

/// <summary>
/// Message sent from ConnectionServer to MainProcess for MSDP updates
/// </summary>
public record MSDPUpdateMessage(long Handle, Dictionary<string, string> Variables);

/// <summary>
/// Message sent from ConnectionServer to MainProcess for MSSP updates
/// </summary>
public record MSSPUpdateMessage(long Handle, Dictionary<string, string> Configuration);

/// <summary>
/// Message sent from ConnectionServer to MainProcess for NAWS (window size) updates
/// </summary>
public record NAWSUpdateMessage(long Handle, int Height, int Width);

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a connection is established
/// </summary>
public record ConnectionEstablishedMessage(
	long Handle,
	string IpAddress,
	string Hostname,
	string ConnectionType,
	DateTimeOffset Timestamp
);

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a connection is closed
/// </summary>
public record ConnectionClosedMessage(long Handle, DateTimeOffset Timestamp);

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a WebSocket client submits input
/// </summary>
public record WebSocketInputMessage(long Handle, string Input);
