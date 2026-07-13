using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Interface for storing and retrieving connection state across processes
/// </summary>
public interface IConnectionStateStore
{
	/// <summary>
	/// Store connection state
	/// </summary>
	Task SetConnectionAsync(long handle, ConnectionStateData data, CancellationToken ct = default);

	/// <summary>
	/// Retrieve connection state
	/// </summary>
	Task<ConnectionStateData?> GetConnectionAsync(long handle, CancellationToken ct = default);

	/// <summary>
	/// Remove connection state
	/// </summary>
	Task RemoveConnectionAsync(long handle, CancellationToken ct = default);

	/// <summary>
	/// Get all active connection handles
	/// </summary>
	Task<IEnumerable<long>> GetAllHandlesAsync(CancellationToken ct = default);

	/// <summary>
	/// Get all active connections with their data
	/// </summary>
	Task<IEnumerable<(long Handle, ConnectionStateData Data)>> GetAllConnectionsAsync(CancellationToken ct = default);

	/// <summary>
	/// Update player binding for a connection
	/// </summary>
	Task SetPlayerBindingAsync(long handle, DBRef? playerRef, CancellationToken ct = default);

	/// <summary>
	/// Update connection metadata
	/// </summary>
	Task UpdateMetadataAsync(long handle, string key, string value, CancellationToken ct = default);
}

/// <summary>
/// Data transfer object for connection state stored in Redis
/// </summary>
public class ConnectionStateData
{
	public required long Handle { get; init; }
	public DBRef? PlayerRef { get; set; }
	public required string State { get; set; }
	public required string IpAddress { get; init; }
	public required string Hostname { get; init; }
	public required string ConnectionType { get; init; }
	public required DateTimeOffset ConnectedAt { get; init; }
	public DateTimeOffset LastSeen { get; set; }
	public Dictionary<string, string> Metadata { get; init; } = new();
}
