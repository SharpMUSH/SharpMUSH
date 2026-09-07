namespace SharpMUSH.Messaging.Messages;

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a player submits input
/// </summary>
public record TelnetInputMessage(long Handle, string Input) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess for GMCP signals
/// </summary>
public record GMCPSignalMessage(long Handle, string Package, string Info) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess for MSDP updates
/// </summary>
public record MSDPUpdateMessage(long Handle, Dictionary<string, string> Variables) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess for MSSP updates
/// </summary>
public record MSSPUpdateMessage(long Handle, Dictionary<string, string> Configuration) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess for NAWS (window size) updates
/// </summary>
public record NAWSUpdateMessage(long Handle, int Height, int Width) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a connection is established
/// </summary>
public record ConnectionEstablishedMessage(
	long Handle,
	string IpAddress,
	string Hostname,
	string ConnectionType,
	DateTimeOffset Timestamp,
	string PresenceClass = "play"
) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a connection is closed
/// </summary>
public record ConnectionClosedMessage(long Handle, DateTimeOffset Timestamp) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a WebSocket client submits input
/// </summary>
public record WebSocketInputMessage(long Handle, string Input) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess when Pueblo handshake is detected.
/// The main process should set PUEBLO metadata on the connection.
/// </summary>
public record PuebloNegotiatedMessage(long Handle, string ClientResponse) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess when MXP (telnet option 91) is negotiated.
/// MXP is a superset of Pueblo — if both negotiate, MXP takes priority.
/// </summary>
public record MxpNegotiatedMessage(long Handle) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess when a client answers RFC 1091 terminal type
/// negotiation. <paramref name="TerminalTypes"/> is the list as the client reported it, in order:
/// MTTS reads the first entry as the client's name, which is what <c>terminfo()</c> reports.
/// </summary>
public record TerminalTypeNegotiatedMessage(long Handle, IReadOnlyList<string> TerminalTypes) : IHandleMessage;

/// <summary>
/// Message sent from ConnectionServer to MainProcess the first time a client genuinely answers a
/// telnet option — PennMUSH's CONN_TELNET, and the "telnet" token in <c>terminfo()</c>. Arriving on
/// the telnet port is not enough on its own: a raw socket never negotiates anything.
/// </summary>
public record TelnetNegotiatedMessage(long Handle) : IHandleMessage;
