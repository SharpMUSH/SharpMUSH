using System.Collections.Concurrent;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests;

/// <summary>The connection store contract in memory, for a host whose connections no other process shares.</summary>
public sealed class InMemoryConnectionStateStore : IConnectionStateStore
{
	private readonly ConcurrentDictionary<long, ConnectionStateData> _state = [];

	public Task SetConnectionAsync(long handle, ConnectionStateData data, CancellationToken ct = default)
	{
		_state[handle] = data;
		return Task.CompletedTask;
	}

	public Task<ConnectionStateData?> GetConnectionAsync(long handle, CancellationToken ct = default) =>
		Task.FromResult(_state.GetValueOrDefault(handle));

	public Task RemoveConnectionAsync(long handle, CancellationToken ct = default)
	{
		_state.TryRemove(handle, out _);
		return Task.CompletedTask;
	}

	public Task<IEnumerable<long>> GetAllHandlesAsync(CancellationToken ct = default) =>
		Task.FromResult<IEnumerable<long>>(_state.Keys.ToArray());

	public Task<IEnumerable<(long Handle, ConnectionStateData Data)>> GetAllConnectionsAsync(
		CancellationToken ct = default) =>
		Task.FromResult<IEnumerable<(long, ConnectionStateData)>>(
			_state.Select(x => (x.Key, x.Value)).ToArray());

	public Task SetPlayerBindingAsync(long handle, string? playerObjid, CancellationToken ct = default)
	{
		if (_state.TryGetValue(handle, out var data)) data.PlayerObjid = playerObjid;
		return Task.CompletedTask;
	}

	public Task UpdateMetadataAsync(long handle, string key, string value, CancellationToken ct = default)
	{
		if (_state.TryGetValue(handle, out var data)) data.Metadata[key] = value;
		return Task.CompletedTask;
	}
}
