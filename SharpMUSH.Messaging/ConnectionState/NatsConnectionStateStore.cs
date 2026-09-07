// NOTE: relocated from SharpMUSH.Library so the ConnectionServer does not depend on the full
// Library. The original SharpMUSH.Library.* namespace is preserved so consumers are unchanged.
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Library.Services;

/// <summary>
/// NATS JetStream Key-Value-backed implementation of connection state store.
/// Uses a JetStream KV bucket with 24-hour TTL and revision-checked mutations.
/// </summary>
public sealed class NatsConnectionStateStore : IConnectionStateStore, IAsyncDisposable
{
	private const string BucketName = "sharpmush-connections";
	private const string KeyPrefix = "conn.";

	private readonly NatsConnection _nats;
	private readonly INatsKVStore _store;
	private readonly ILogger<NatsConnectionStateStore> _logger;

	internal NatsConnectionStateStore(NatsConnection nats, INatsKVStore store, ILogger<NatsConnectionStateStore> logger)
	{
		_nats = nats;
		_store = store;
		_logger = logger;
	}

	/// <summary>
	/// Creates and initialises a <see cref="NatsConnectionStateStore"/>.
	/// Creates the JetStream KV bucket if it does not already exist.
	/// </summary>
	public static async Task<NatsConnectionStateStore> CreateAsync(
		string url,
		ILogger<NatsConnectionStateStore> logger,
		CancellationToken ct = default)
	{
		var nats = new NatsConnection(new NatsOpts { Url = url });
		await nats.ConnectAsync();
		var js = new NatsJSContext(nats);
		var kv = new NatsKVContext(js);
		var store = await kv.CreateOrUpdateStoreAsync(
			new NatsKVConfig(BucketName) { MaxAge = TimeSpan.FromHours(24) },
			ct);
		return new NatsConnectionStateStore(nats, store, logger);
	}

	public async Task SetConnectionAsync(long handle, ConnectionStateData data, CancellationToken ct = default)
	{
		try
		{
			var json = JsonSerializer.Serialize(data);
			await _store.PutAsync(GetKey(handle), json, cancellationToken: ct);
			_logger.LogTrace("Stored connection state for handle {Handle}", handle);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to store connection state for handle {Handle}", handle);
			throw;
		}
	}

	public async Task<ConnectionStateData?> GetConnectionAsync(long handle, CancellationToken ct = default)
	{
		try
		{
			var result = await _store.TryGetEntryAsync<string>(GetKey(handle), cancellationToken: ct);
			if (!result.Success)
			{
				if (result.Error is not (NatsKVKeyNotFoundException or NatsKVKeyDeletedException))
					throw result.Error;
				_logger.LogTrace("No connection state found for handle {Handle}", handle);
				return null;
			}

			var json = result.Value.Value;
			return json is null ? null : JsonSerializer.Deserialize<ConnectionStateData>(json);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to retrieve connection state for handle {Handle}", handle);
			throw;
		}
	}

	public async Task RemoveConnectionAsync(long handle, CancellationToken ct = default)
	{
		try
		{
			await _store.DeleteAsync(GetKey(handle), cancellationToken: ct);
			_logger.LogTrace("Removed connection state for handle {Handle}", handle);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to remove connection state for handle {Handle}", handle);
			throw;
		}
	}

	public async Task<IEnumerable<long>> GetAllHandlesAsync(CancellationToken ct = default)
	{
		try
		{
			var handles = new List<long>();
			// IgnoreDeletes filters out tombstone entries left by DeleteAsync
			await foreach (var key in _store.GetKeysAsync(
				new NatsKVWatchOpts { IgnoreDeletes = true },
				ct))
			{
				if (key.StartsWith(KeyPrefix, StringComparison.Ordinal)
					&& long.TryParse(key.AsSpan(KeyPrefix.Length), out var handle))
				{
					handles.Add(handle);
				}
			}

			return handles;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to retrieve all connection handles");
			throw;
		}
	}

	public async Task<IEnumerable<(long Handle, ConnectionStateData Data)>> GetAllConnectionsAsync(CancellationToken ct = default)
	{
		try
		{
			var handles = await GetAllHandlesAsync(ct);
			var connections = new List<(long, ConnectionStateData)>();
			foreach (var handle in handles)
			{
				try
				{
					var data = await GetConnectionAsync(handle, ct);
					if (data is not null)
						connections.Add((handle, data));
				}
				catch (JsonException ex)
				{
					_logger.LogWarning(ex, "Skipping malformed connection state for handle {Handle}", handle);
				}
			}

			return connections;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to retrieve all connections");
			throw;
		}
	}

	public Task SetPlayerBindingAsync(long handle, string? playerObjid, CancellationToken ct = default) =>
		MutateAsync(handle, data =>
		{
			data.PlayerObjid = playerObjid;
			data.State = playerObjid is not null ? "LoggedIn" : "Connected";
		}, ct);

	public Task UpdateMetadataAsync(long handle, string key, string value, CancellationToken ct = default) =>
		MutateAsync(handle, data =>
		{
			data.Metadata[key] = value;
			if (key == "State") data.State = value;
		}, ct);

	public Task<bool> TryRevokeResumeAsync(long handle, string sessionId, CancellationToken ct = default) =>
		string.IsNullOrWhiteSpace(sessionId) ? Task.FromResult(false)
			: MutateAsync(handle, data => data.Metadata["ResumeRevoked"] = "1", ct, sessionId);

	public Task<bool> TryUpdateTransportAsync(long handle, string sessionId, string? playerObjid, string state,
		string ip, string host, bool secure, CancellationToken ct = default) =>
		string.IsNullOrWhiteSpace(sessionId) ? Task.FromResult(false) : MutateAsync(handle, data =>
		{
			data.Metadata["InternetProtocolAddress"] = ip;
			data.Metadata["HostName"] = host;
			data.Metadata["SSL"] = secure ? "1" : "0";
		}, ct, sessionId, data =>
		{
			if (data.Metadata.GetValueOrDefault("ResumeRevoked") == "1"
				|| data.PlayerObjid != playerObjid || data.State != state || data.ConnectionType != "websocket"
				|| (!secure && data.Metadata.GetValueOrDefault("SSL") == "1")) return false;
			return !data.Metadata.TryGetValue("ResumeExpiresAt", out var expiry)
				|| (long.TryParse(expiry, out var expiresAt) && expiresAt >= 0
					&& (expiresAt == 0 || expiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
		});

	private async Task<bool> MutateAsync(long handle, Action<ConnectionStateData> mutate, CancellationToken ct,
		string? sessionId = null, Func<ConnectionStateData, bool>? validate = null)
	{
		DateTimeOffset? incarnation = null;
		for (var attempt = 0; attempt < 16; attempt++)
		{
			var entry = await _store.TryGetEntryAsync<string>(GetKey(handle), cancellationToken: ct);
			if (!entry.Success)
			{
				if (entry.Error is NatsKVKeyNotFoundException or NatsKVKeyDeletedException) return false;
				throw entry.Error;
			}
			var data = entry.Value.Value is { } json ? JsonSerializer.Deserialize<ConnectionStateData>(json) : null;
			if (data is null || data.Handle != handle
				|| (sessionId is not null && data.Metadata.GetValueOrDefault("SessionId") != sessionId)) return false;
			// Never apply a retry to a later occupant of a recycled descriptor.
			if (incarnation is not null && incarnation != data.ConnectedAt) return false;
			incarnation = data.ConnectedAt;
			if (validate is not null && !validate(data)) return false;
			mutate(data);
			data.LastSeen = DateTimeOffset.UtcNow;
			var result = await _store.TryUpdateAsync(GetKey(handle), JsonSerializer.Serialize(data),
				entry.Value.Revision, cancellationToken: ct);
			if (result.Success) return true;
			if (result.Error is not NatsKVWrongLastRevisionException) throw result.Error;
			await Task.Delay(SharpMUSH.Library.Utilities.ConnectionRetryPolicy.Delay, ct);
		}
		throw new InvalidOperationException($"Connection {handle} changed repeatedly while updating state.");
	}

	private static string GetKey(long handle) => $"{KeyPrefix}{handle}";

	public async ValueTask DisposeAsync()
	{
		await _nats.DisposeAsync();
	}
}
