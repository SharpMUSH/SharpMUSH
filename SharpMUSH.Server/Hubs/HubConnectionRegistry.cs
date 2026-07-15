using System.Collections.Concurrent;

namespace SharpMUSH.Server.Hubs;

/// <summary>
/// In-process singleton tracking live SignalR connections on <see cref="GameHub"/>, keyed by
/// connection id, so that ban enforcement can locate and force-abort every connection belonging
/// to a banned account or originating IP.
///
/// <see cref="GameHub"/> populates this registry in <c>OnConnectedAsync</c> (from the
/// authenticated user's account id claim and the connection's remote IP) and clears it in
/// <c>OnDisconnectedAsync</c>.
/// </summary>
/// <remarks>
/// Each entry also carries an <c>Abort</c> delegate (bound to <c>HubCallerContext.Abort()</c> at
/// connect time), because SignalR exposes no <c>IHubContext.Abort(connectionId)</c> API — capturing
/// the delegate per-connection is the only way a downstream ban-enforcement service can actually
/// terminate a live connection rather than merely enumerate it.
/// </remarks>
public sealed class HubConnectionRegistry
{
	private readonly record struct Entry(string AccountId, string OriginIp, Action Abort);

	private readonly ConcurrentDictionary<string, Entry> _connections = new(StringComparer.Ordinal);

	/// <summary>
	/// Registers a live connection. <paramref name="abort"/> is invoked by
	/// <see cref="AbortConnectionsForAccount"/> / <see cref="AbortConnectionsForIp"/> to force-terminate
	/// this connection.
	/// </summary>
	public void Add(string connectionId, string accountId, string originIp, Action abort) =>
		_connections[connectionId] = new Entry(accountId, originIp, abort);

	/// <summary>Deregisters a connection, typically called from <c>OnDisconnectedAsync</c>.</summary>
	public void Remove(string connectionId) => _connections.TryRemove(connectionId, out _);

	/// <summary>Returns every live connection id belonging to the given account.</summary>
	public IReadOnlyList<string> ConnectionsForAccount(string accountId) =>
		_connections
			.Where(pair => pair.Value.AccountId == accountId)
			.Select(pair => pair.Key)
			.ToList();

	/// <summary>Returns every live connection id that originated from the given IP.</summary>
	public IReadOnlyList<string> ConnectionsForIp(string originIp) =>
		_connections
			.Where(pair => pair.Value.OriginIp == originIp)
			.Select(pair => pair.Key)
			.ToList();

	/// <summary>Force-aborts every live connection belonging to the given account.</summary>
	public void AbortConnectionsForAccount(string accountId)
	{
		foreach (var pair in _connections.Where(pair => pair.Value.AccountId == accountId))
			pair.Value.Abort();
	}

	/// <summary>Force-aborts every live connection that originated from the given IP.</summary>
	public void AbortConnectionsForIp(string originIp)
	{
		foreach (var pair in _connections.Where(pair => pair.Value.OriginIp == originIp))
			pair.Value.Abort();
	}
}
