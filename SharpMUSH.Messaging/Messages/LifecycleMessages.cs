namespace SharpMUSH.Messaging.Messages;

/// <summary>
/// Message sent from MainProcess to ConnectionServer when MainProcess is ready to process commands
/// </summary>
public record MainProcessReadyMessage(DateTimeOffset Timestamp, string Version);

/// <summary>
/// Message sent from MainProcess to ConnectionServer when MainProcess is shutting down
/// </summary>
public record MainProcessShutdownMessage(DateTimeOffset Timestamp, string? Reason);

/// <summary>
/// Message sent from MainProcess to ConnectionServer when MainProcess has reconnected after restart
/// </summary>
public record MainProcessReconnectedMessage(DateTimeOffset Timestamp, string Version);
