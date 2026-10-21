using System.Text.Json;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests;

/// <summary>
/// The connection store contract in memory, for a host whose connections no other process shares.
/// Like the NATS store, each entry is a serialized copy and each change a read-modify-write of it,
/// so callers never share a <see cref="ConnectionStateData"/>; the resume mutations make the same
/// session and transport checks.
/// </summary>
public sealed class InMemoryConnectionStateStore : IConnectionStateStore
{
	private readonly Lock _gate = new();
	private readonly Dictionary<long, string> _state = [];

	public Task SetConnectionAsync(long handle, ConnectionStateData data, CancellationToken ct = default)
	{
		var json = JsonSerializer.Serialize(data);
		lock (_gate) _state[handle] = json;
		return Task.CompletedTask;
	}

	public Task<ConnectionStateData?> GetConnectionAsync(long handle, CancellationToken ct = default)
	{
		lock (_gate) return Task.FromResult(_state.TryGetValue(handle, out var json) ? Read(json) : null);
	}

	public Task RemoveConnectionAsync(long handle, CancellationToken ct = default)
	{
		lock (_gate) _state.Remove(handle);
		return Task.CompletedTask;
	}

	public Task<IEnumerable<long>> GetAllHandlesAsync(CancellationToken ct = default)
	{
		lock (_gate) return Task.FromResult<IEnumerable<long>>(_state.Keys.ToArray());
	}

	public Task<IEnumerable<(long Handle, ConnectionStateData Data)>> GetAllConnectionsAsync(
		CancellationToken ct = default)
	{
		lock (_gate)
			return Task.FromResult<IEnumerable<(long, ConnectionStateData)>>(
				_state.Select(x => (x.Key, Read(x.Value))).ToArray());
	}

	public Task SetPlayerBindingAsync(long handle, string? playerObjid, CancellationToken ct = default) =>
		Task.FromResult(Mutate(handle, data => data.PlayerObjid = playerObjid));

	public Task UpdateMetadataAsync(long handle, string key, string value, CancellationToken ct = default) =>
		Task.FromResult(Mutate(handle, data => data.Metadata[key] = value));

	public Task<bool> TryRevokeResumeAsync(long handle, string sessionId, CancellationToken ct = default) =>
		Task.FromResult(!string.IsNullOrWhiteSpace(sessionId)
			&& Mutate(handle, data => data.Metadata["ResumeRevoked"] = "1", sessionId));

	public Task<bool> TryUpdateTransportAsync(long handle, string sessionId, string? playerObjid, string state,
		string ip, string host, bool secure, bool orderedPrompts, CancellationToken ct = default) =>
		Task.FromResult(!string.IsNullOrWhiteSpace(sessionId) && Mutate(handle, data =>
		{
			data.Metadata["InternetProtocolAddress"] = ip;
			data.Metadata["HostName"] = host;
			data.Metadata["SSL"] = secure ? "1" : "0";
			data.Metadata[ConnectionEstablishedMessage.OrderedPromptsMetadata] = orderedPrompts ? "1" : "0";
		}, sessionId, data =>
		{
			if (data.Metadata.GetValueOrDefault("ResumeRevoked") == "1"
				|| data.PlayerObjid != playerObjid || data.State != state || data.ConnectionType != "websocket"
				|| (!secure && data.Metadata.GetValueOrDefault("SSL") == "1")) return false;
			return !data.Metadata.TryGetValue("ResumeExpiresAt", out var expiry)
				|| (long.TryParse(expiry, out var expiresAt) && expiresAt >= 0
					&& (expiresAt == 0 || expiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
		}));

	private bool Mutate(long handle, Action<ConnectionStateData> mutate, string? sessionId = null,
		Func<ConnectionStateData, bool>? validate = null)
	{
		lock (_gate)
		{
			if (!_state.TryGetValue(handle, out var json)) return false;
			var data = Read(json);
			if (sessionId is not null && data.Metadata.GetValueOrDefault("SessionId") != sessionId) return false;
			if (validate is not null && !validate(data)) return false;
			mutate(data);
			_state[handle] = JsonSerializer.Serialize(data);
			return true;
		}
	}

	private static ConnectionStateData Read(string json) => JsonSerializer.Deserialize<ConnectionStateData>(json)!;
}
