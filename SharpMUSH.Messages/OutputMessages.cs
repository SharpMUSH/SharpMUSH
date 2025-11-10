namespace SharpMUSH.Messages;

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output text to a specific connection
/// </summary>
public record TelnetOutputMessage(long Handle, byte[] Data);

/// <summary>
/// Message sent from MainProcess to ConnectionServer to output a prompt to a specific connection
/// </summary>
public record TelnetPromptMessage(long Handle, byte[] Data);

/// <summary>
/// Message sent from MainProcess to ConnectionServer to broadcast to all connections
/// </summary>
public record BroadcastMessage(byte[] Data);

/// <summary>
/// Message sent from MainProcess to ConnectionServer to bind a connection to a player
/// </summary>
public record BindConnectionMessage(long Handle, string PlayerDbRef);

/// <summary>
/// Message sent from MainProcess to ConnectionServer to disconnect a connection
/// </summary>
public record DisconnectConnectionMessage(long Handle, string? Reason);

/// <summary>
/// Message sent from MainProcess to ConnectionServer to update connection metadata
/// </summary>
public record UpdateConnectionMetadataMessage(long Handle, string Key, string Value);
