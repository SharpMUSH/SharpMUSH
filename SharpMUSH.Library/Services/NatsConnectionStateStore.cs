using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.KeyValueStore;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.Json;

namespace SharpMUSH.Library.Services;

/// <summary>
/// NATS JetStream Key-Value-backed implementation of connection state store.
/// Adapter alongside <see cref="RedisConnectionStateStore"/> for performance comparison.
/// Uses a JetStream KV bucket with 24-hour TTL; CAS-based optimistic concurrency
/// mirrors the Redis WATCH/transaction pattern in the Redis implementation.
/// </summary>
public sealed class NatsConnectionStateStore : IConnectionStateStore, IAsyncDisposable
{
	private const string BucketName = "sharpmush-connections";
	private const string KeyPrefix = "conn.";

	private readonly NatsConnection _nats;
	private readonly INatsKVStore _store;
	private readonly ILogger<NatsConnectionStateStore> _logger;

	private NatsConnectionStateStore(NatsConnection nats, INatsKVStore store, ILogger<NatsConnectionStateStore> logger)
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
			_logger.LogDebug("Stored connection state for handle {Handle}", handle);
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
				_logger.LogDebug("No connection state found for handle {Handle}", handle);
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
			_logger.LogDebug("Removed connection state for handle {Handle}", handle);
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
				var data = await GetConnectionAsync(handle, ct);
				if (data is not null)
					connections.Add((handle, data));
			}

			return connections;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to retrieve all connections");
			throw;
		}
	}

	public async Task SetPlayerBindingAsync(long handle, DBRef? playerRef, CancellationToken ct = default)
	{
		try
		{
			var data = await GetConnectionAsync(handle, ct);
			if (data is null)
			{
				_logger.LogWarning("Cannot set player binding for non-existent connection {Handle}", handle);
				return;
			}

			data.PlayerRef = playerRef;
			data.State = playerRef.HasValue ? "LoggedIn" : "Connected";
			data.LastSeen = DateTimeOffset.UtcNow;
			await SetConnectionAsync(handle, data, ct);
			_logger.LogDebug("Updated player binding for handle {Handle} to {PlayerRef}", handle, playerRef);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to set player binding for handle {Handle}", handle);
			throw;
		}
	}

	public async Task UpdateMetadataAsync(long handle, string key, string value, CancellationToken ct = default)
	{
		try
		{
			var connectionKey = GetKey(handle);
			const int maxRetries = 10;

			for (var retries = 0; retries < maxRetries; retries++)
			{
				// Read current entry including revision for CAS
				var readResult = await _store.TryGetEntryAsync<string>(connectionKey, cancellationToken: ct);
				if (!readResult.Success)
				{
					_logger.LogWarning("Cannot update metadata for non-existent connection {Handle}", handle);
					return;
				}

				var entry = readResult.Value;
				var data = JsonSerializer.Deserialize<ConnectionStateData>(entry.Value!)!;
				data.Metadata[key] = value;
				data.LastSeen = DateTimeOffset.UtcNow;
				var newJson = JsonSerializer.Serialize(data);

				// CAS: only succeeds if no concurrent write occurred
				var updateResult = await _store.TryUpdateAsync(connectionKey, newJson, entry.Revision, cancellationToken: ct);
				if (updateResult.Success)
				{
					_logger.LogDebug("Updated metadata for handle {Handle}: {Key}={Value}", handle, key, value);
					return;
				}

				// Concurrent modification — back off and retry
				await Task.Delay(10 * (retries + 1), ct);
			}

			_logger.LogWarning("Failed to update metadata after {Retries} retries for handle {Handle}", maxRetries, handle);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to update metadata for handle {Handle}", handle);
			throw;
		}
	}

	private static string GetKey(long handle) => $"{KeyPrefix}{handle}";

	public async ValueTask DisposeAsync()
	{
		await _nats.DisposeAsync();
	}
}
