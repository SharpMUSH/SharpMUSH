namespace SharpMUSH.Messages;

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

/// <summary>
/// Message sent from ConnectionServer to MainProcess requesting connection state synchronization
/// </summary>
public record RequestConnectionStateSyncMessage(DateTimeOffset Timestamp);

/// <summary>
/// Message sent from MainProcess to ConnectionServer with connection state information
/// </summary>
public record ConnectionStateSyncMessage(DateTimeOffset Timestamp, List<ConnectionStateInfo> Connections);

/// <summary>
/// Information about a connection's state
/// </summary>
public record ConnectionStateInfo(
	long Handle,
	string? PlayerDbRef,
	string State,
	DateTimeOffset ConnectedAt,
	DateTimeOffset LastActivity
);
