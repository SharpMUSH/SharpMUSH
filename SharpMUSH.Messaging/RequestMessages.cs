namespace SharpMUSH.Messages;

/// <summary>
/// Request for server information (API call)
/// </summary>
public record GetServerInfoRequest();

/// <summary>
/// Response with server information
/// </summary>
public record GetServerInfoResponse(
	string Version,
	DateTimeOffset StartTime,
	TimeSpan Uptime,
	int PlayerCount,
	int ObjectCount,
	int RoomCount
);

/// <summary>
/// Request for player information (API call)
/// </summary>
public record GetPlayerInfoRequest(string PlayerDbRef);

/// <summary>
/// Response with player information
/// </summary>
public record GetPlayerInfoResponse(
	string PlayerDbRef,
	string Name,
	bool IsConnected,
	DateTimeOffset? LastConnected,
	List<long> ConnectionHandles
);

/// <summary>
/// Request to get all connected players (API call)
/// </summary>
public record GetConnectedPlayersRequest();

/// <summary>
/// Response with list of connected players
/// </summary>
public record GetConnectedPlayersResponse(List<ConnectedPlayerInfo> Players);

/// <summary>
/// Information about a connected player
/// </summary>
public record ConnectedPlayerInfo(
	string PlayerDbRef,
	string Name,
	DateTimeOffset ConnectedAt,
	TimeSpan IdleTime,
	int ConnectionCount
);
