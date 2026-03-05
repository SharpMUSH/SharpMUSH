namespace SharpMUSH.Messages;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output text to a specific connection
/// </summary>
public record TelnetOutputMessage(long Handle, byte[] Data) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output a prompt to a specific connection
/// </summary>
public record TelnetPromptMessage(long Handle, byte[] Data) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to broadcast to all connections
/// </summary>
public record BroadcastMessage(byte[] Data);

/// <summary>
/// Message sent from MainProcess to ConnectionServer to disconnect a connection
/// </summary>
public record DisconnectConnectionMessage(long Handle, string? Reason) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output text to a WebSocket connection
/// </summary>
public record WebSocketOutputMessage(long Handle, string Data) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output a prompt to a WebSocket connection
/// </summary>
public record WebSocketPromptMessage(long Handle, string Data) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to send GMCP data to a connection
/// </summary>
public record GMCPOutputMessage(long Handle, string Module, string Message) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to send MSDP variable updates to a connection
/// </summary>
public record MSDPOutputMessage(long Handle, Dictionary<string, string> Variables) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to send MSSP configuration to a connection
/// </summary>
public record MSSPOutputMessage(long Handle, Dictionary<string, string> Configuration) : IHandleMessage;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to update player output preferences for a connection
/// </summary>
public record UpdatePlayerPreferencesMessage(
	long Handle,
	bool AnsiEnabled,
	bool ColorEnabled,
	bool Xterm256Enabled
) : IHandleMessage;