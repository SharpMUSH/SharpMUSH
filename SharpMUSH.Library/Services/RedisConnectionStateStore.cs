using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;
using StackExchange.Redis;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Redis-backed implementation of connection state store.
/// Provides shared state across ConnectionServer and Server processes.
/// </summary>
public class RedisConnectionStateStore : IConnectionStateStore, IAsyncDisposable
{
	private readonly IConnectionMultiplexer _redis;
	private readonly IDatabase _db;
	private readonly ILogger<RedisConnectionStateStore> _logger;
	private readonly TimeSpan _defaultExpiry = TimeSpan.FromHours(24);
	private const string ConnectionKeyPrefix = "sharpmush:conn:";
	private const string ConnectionSetKey = "sharpmush:conn:active";

	public RedisConnectionStateStore(
		IConnectionMultiplexer redis,
		ILogger<RedisConnectionStateStore> logger)
	{
		_redis = redis ?? throw new ArgumentNullException(nameof(redis));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		_db = _redis.GetDatabase();
	}

	public async Task SetConnectionAsync(long handle, ConnectionStateData data, CancellationToken ct = default)
	{
		try
		{
			var key = GetConnectionKey(handle);
			var json = JsonSerializer.Serialize(data);

			// Store connection data with expiry
			await _db.StringSetAsync(key, json, _defaultExpiry);

			// Add to active connections set
			await _db.SetAddAsync(ConnectionSetKey, handle);

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
			var key = GetConnectionKey(handle);
			var json = await _db.StringGetAsync(key);

			if (!json.HasValue)
			{
				_logger.LogDebug("No connection state found for handle {Handle}", handle);
				return null;
			}

			var data = JsonSerializer.Deserialize<ConnectionStateData>(json.ToString());
			return data;
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
			var key = GetConnectionKey(handle);

			// Remove connection data
			await _db.KeyDeleteAsync(key);

			// Remove from active connections set
			await _db.SetRemoveAsync(ConnectionSetKey, handle);

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
			var handles = await _db.SetMembersAsync(ConnectionSetKey);
			return handles.Select(v => (long)v).ToList();
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
				if (data != null)
				{
					connections.Add((handle, data));
				}
				else
				{
					// Clean up orphaned handle in set
					await _db.SetRemoveAsync(ConnectionSetKey, handle);
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

	public async Task SetPlayerBindingAsync(long handle, DBRef? playerRef, CancellationToken ct = default)
	{
		try
		{
			var data = await GetConnectionAsync(handle, ct);
			if (data == null)
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
			var connectionKey = GetConnectionKey(handle);
			
			// Use optimistic locking with transactions to handle concurrent updates
			var retries = 0;
			const int maxRetries = 10;
			
			while (retries < maxRetries)
			{
				// Watch the key for changes
				var data = await GetConnectionAsync(handle, ct);
				if (data == null)
				{
					_logger.LogWarning("Cannot update metadata for non-existent connection {Handle}", handle);
					return;
				}

				// Start transaction
				var tran = _db.CreateTransaction();
				
				// Add condition - key must not have changed
				tran.AddCondition(Condition.StringEqual(connectionKey, JsonSerializer.Serialize(data)));
				
				// Update metadata
				data.Metadata[key] = value;
				data.LastSeen = DateTimeOffset.UtcNow;
				
				// Queue the update
				var json = JsonSerializer.Serialize(data);
				_ = tran.StringSetAsync(connectionKey, json, _defaultExpiry);
				
				// Execute transaction
				if (await tran.ExecuteAsync())
				{
					_logger.LogDebug("Updated metadata for handle {Handle}: {Key}={Value}", handle, key, value);
					return;
				}
				
				// Transaction failed due to concurrent modification, retry
				retries++;
				await Task.Delay(10 * retries, ct); // Exponential backoff
			}
			
			_logger.LogWarning("Failed to update metadata after {Retries} retries for handle {Handle}", maxRetries, handle);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to update metadata for handle {Handle}", handle);
			throw;
		}
	}

	private static string GetConnectionKey(long handle) => $"{ConnectionKeyPrefix}{handle}";

	public async ValueTask DisposeAsync()
	{
		if (_redis != null)
		{
			await _redis.CloseAsync();
			_redis.Dispose();
		}
	}
}
