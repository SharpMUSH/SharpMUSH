using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Manages active connections in the ConnectionServer
/// </summary>
public class ConnectionServerService(
	ILogger<ConnectionServerService> logger,
	IMessageBus publishEndpoint,
	IConnectionStateStore? stateStore = null,
	IHostApplicationLifetime? lifetime = null) : IConnectionServerService
{
	private readonly ConcurrentDictionary<long, ConnectionData> _sessionState = [];

	public async Task RegisterAsync(
		long handle,
		string ipAddress,
		string hostname,
		string connectionType,
		Func<byte[], ValueTask> outputFunction,
		Func<byte[], ValueTask> promptOutputFunction,
		Func<Encoding> encodingFunction,
		Action disconnectFunction,
		Func<string, string, ValueTask>? gmcpFunction = null,
		ProtocolCapabilities? capabilities = null,
		string presenceClass = "play",
		bool isSecure = false,
		string? sessionId = null,
		CancellationToken cancellationToken = default)
	{
		sessionId ??= Guid.NewGuid().ToString("N");
		var connectedAt = DateTimeOffset.UtcNow;
		var data = new ConnectionData(handle, null, ConnectionState.Connected, outputFunction,
			promptOutputFunction, encodingFunction, disconnectFunction, gmcpFunction,
			capabilities ?? new ProtocolCapabilities(), null, connectionType, presenceClass, sessionId);
		if (!_sessionState.TryAdd(handle, data)) throw new InvalidOperationException("Handle already registered");

		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
			lifetime?.ApplicationStopping ?? CancellationToken.None);
		deadline.CancelAfter(TimeSpan.FromSeconds(30));
		var publishAttempted = false;
		try
		{
			if (stateStore is not null)
			{
				var persisted = new ConnectionStateData
				{
					Handle = handle,
					PlayerObjid = null,
					State = "Connected",
					IpAddress = ipAddress,
					Hostname = hostname,
					ConnectionType = connectionType,
					ConnectedAt = connectedAt,
					LastSeen = connectedAt,
					Metadata = new Dictionary<string, string>
					{
						{ "ConnectionStartTime", connectedAt.ToUnixTimeMilliseconds().ToString() },
						{ "LastConnectionSignal", connectedAt.ToUnixTimeMilliseconds().ToString() },
						{ "InternetProtocolAddress", ipAddress },
						{ "HostName", hostname },
						{ "ConnectionType", connectionType },
						{ "PresenceClass", presenceClass },
						{ "SessionId", sessionId },
						{ "SSL", isSecure ? "1" : "0" }
					}
				};
				// The engine rejects registration without this authoritative record. Complete the
				// durable phase before publishing; retries use the same incarnation and timestamps.
				await RetryRegistrationStepAsync(handle, "persist", ct => stateStore.SetConnectionAsync(handle, persisted, ct), deadline.Token);
			}

			var established = new ConnectionEstablishedMessage(handle, ipAddress, hostname,
				connectionType, connectedAt, presenceClass, isSecure, sessionId);
			publishAttempted = true;
			// Publication may have succeeded when its acknowledgement was lost. Re-publish the
			// idempotent event, but never overwrite KV again after the engine can bind a player.
			await RetryRegistrationStepAsync(handle, "publish", ct => publishEndpoint.Publish(established, ct), deadline.Token);
			logger.LogInformation("Registered connection handle {Handle} from {IpAddress} ({Type})", handle, ipAddress, connectionType);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Connection registration failed for {Handle}; closing the incomplete session", handle);
			while (_sessionState.TryGetValue(handle, out var current) && current.SessionId == sessionId)
			{
				if (!_sessionState.TryRemove(new KeyValuePair<long, ConnectionData>(handle, current))) continue;
				try { current.DisconnectFunction(); }
				catch (Exception closeError) { logger.LogWarning(closeError, "Could not close incomplete connection {Handle}", handle); }
				break;
			}
			using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
			try
			{
				if (stateStore is not null)
					await stateStore.RemoveConnectionAsync(handle, cleanup.Token).WaitAsync(cleanup.Token);
			}
			catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Could not remove incomplete state for {Handle}", handle); }
			if (publishAttempted)
			{
				try
				{
					await publishEndpoint.Publish(new ConnectionClosedMessage(handle, DateTimeOffset.UtcNow, sessionId), cleanup.Token)
						.WaitAsync(cleanup.Token);
				}
				catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Could not announce incomplete session closure for {Handle}", handle); }
			}
			throw;
		}
	}

	private async Task RetryRegistrationStepAsync(long handle, string step,
		Func<CancellationToken, Task> operation, CancellationToken ct)
	{
		for (var attempt = 0; ; attempt++)
		{
			ct.ThrowIfCancellationRequested();
			try
			{
				await operation(ct).WaitAsync(ct);
				return;
			}
			catch (Exception ex) when (!ct.IsCancellationRequested && attempt + 1 < ConnectionRetryPolicy.MaxAttempts)
			{
				logger.LogWarning(ex, "Could not {Step} connection {Handle}; retrying registration ({Attempt})", step, handle, attempt + 1);
				await Task.Delay(ConnectionRetryPolicy.Delay, ct);
			}
		}
	}

	public void RestoreDormant(ConnectionStateData data, Func<byte[], ValueTask> output, Action disconnect)
	{
		if (!_sessionState.TryAdd(data.Handle, new ConnectionData(data.Handle, data.PlayerObjid,
			data.PlayerObjid is null ? ConnectionState.Connected : ConnectionState.LoggedIn,
			output, output, () => Encoding.UTF8, disconnect, null, new ProtocolCapabilities(), null,
			data.ConnectionType, data.Metadata.GetValueOrDefault("PresenceClass", "play"),
			data.Metadata.GetValueOrDefault("SessionId", ""))))
			throw new InvalidOperationException("A live connection already owns the restored descriptor.");
	}

	public async Task DisconnectAsync(long handle, CancellationToken cancellationToken = default)
	{
		using var teardown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		teardown.CancelAfter(TimeSpan.FromSeconds(2));
		logger.LogInformation("Disconnecting handle {Handle}", handle);
		if (_sessionState.TryRemove(handle, out var data))
		{
			logger.LogInformation("Removed connection handle {Handle} from session state", handle);
			if (stateStore != null)
			{
				try
				{
					await stateStore.RemoveConnectionAsync(handle, teardown.Token).WaitAsync(teardown.Token);
				}
				catch (Exception ex)
				{
					logger.LogWarning(ex, "Failed to remove connection state from NATS KV for handle {Handle}; continuing with publish", handle);
				}
			}

			logger.LogDebug("[NATS-PUBLISH] Publishing ConnectionClosedMessage - Handle: {Handle}, Timestamp: {Timestamp}",
				handle, DateTimeOffset.UtcNow);

			try
			{
				await publishEndpoint.Publish(new ConnectionClosedMessage(handle, DateTimeOffset.UtcNow, data.SessionId), teardown.Token).WaitAsync(teardown.Token);
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Could not publish disconnect for {Handle}; completing local cleanup", handle);
			}
			finally
			{
				data.DisconnectFunction();
			}

		}

	}

	public ConnectionData? Get(long handle) =>
		_sessionState.GetValueOrDefault(handle);

	public IEnumerable<ConnectionData> GetAll() =>
		_sessionState.Values;

	public bool UpdatePreferences(long handle, PlayerOutputPreferences preferences)
	{
		if (_sessionState.TryGetValue(handle, out var connection))
		{
			var updated = connection with { Preferences = preferences };
			for (var attempt = 0; attempt < ConnectionRetryPolicy.MaxAttempts; attempt++)
			{
				if (_sessionState.TryUpdate(handle, updated, connection))
				{
					return true;
				}

				if (!_sessionState.TryGetValue(handle, out connection))
				{
					return false;
				}

				updated = connection with { Preferences = preferences };
			}
		}
		return false;
	}

	public bool ClearPreferences(long handle)
	{
		if (!_sessionState.TryGetValue(handle, out var connection))
		{
			return false;
		}

		for (var attempt = 0; attempt < ConnectionRetryPolicy.MaxAttempts; attempt++)
		{
			if (_sessionState.TryUpdate(handle, connection with { Preferences = null }, connection))
			{
				return true;
			}

			if (!_sessionState.TryGetValue(handle, out connection))
			{
				return false;
			}
		}

		return false;
	}

	/// <summary>
	/// Pins — or, with a null <paramref name="style"/>, unpins — the connection's colour style.
	/// </summary>
	public bool UpdateColorStyle(long handle, string? style) =>
		UpdateCapabilities(handle, current => current with { ColorStylePin = style });

	/// <summary>
	/// Applies <paramref name="change"/> to the connection's capabilities under a compare-and-swap.
	/// <para>
	/// A transform rather than a value, because the capabilities record has several independent
	/// writers — terminal-type negotiation, the Pueblo and MXP format switches, and a
	/// <c>SOCKSET colorstyle</c> pin arriving from the engine — and every one of them used to read the
	/// record, build a whole replacement from it, and write that replacement back. Anything another
	/// writer had set in between was overwritten by a snapshot taken before it existed: a pin
	/// acknowledged to the player, then silently reverted to automatic rendering because the client
	/// happened to finish negotiating in the same instant. Recomputing inside the loop makes each
	/// writer's change apply to whatever is there now, so they compose instead of racing.
	/// </para>
	/// </summary>
	/// <returns>True when the capabilities actually changed; false for an unknown handle or a no-op.</returns>
	public bool UpdateCapabilities(long handle, Func<ProtocolCapabilities, ProtocolCapabilities> change)
	{
		if (!_sessionState.TryGetValue(handle, out var connection))
		{
			return false;
		}

		for (var attempt = 0; attempt < ConnectionRetryPolicy.MaxAttempts; attempt++)
		{
			var capabilities = change(connection.Capabilities);

			if (capabilities == connection.Capabilities)
			{
				return false;
			}

			if (_sessionState.TryUpdate(handle, connection with { Capabilities = capabilities }, connection))
			{
				return true;
			}

			if (!_sessionState.TryGetValue(handle, out connection))
			{
				return false;
			}
		}

		return false;
	}

	public record ConnectionData(
		long Handle,
		string? PlayerDbRef,
		ConnectionState State,
		Func<byte[], ValueTask> OutputFunction,
		Func<byte[], ValueTask> PromptOutputFunction,
		Func<Encoding> EncodingFunction,
		Action DisconnectFunction,
		Func<string, string, ValueTask>? GMCPFunction,
		ProtocolCapabilities Capabilities,
		PlayerOutputPreferences? Preferences,
		string ConnectionType = "telnet",
		string PresenceClass = "play",
		string SessionId = "");

	public enum ConnectionState
	{
		Connected,
		LoggedIn,
		Disconnected
	}
}

public interface IConnectionServerService
{
	Task RegisterAsync(long handle, string ipAddress, string hostname, string connectionType,
		Func<byte[], ValueTask> outputFunction, Func<byte[], ValueTask> promptOutputFunction,
		Func<Encoding> encodingFunction,
		Action disconnectFunction,
		Func<string, string, ValueTask>? gmcpFunction = null,
		SharpMUSH.ConnectionServer.Models.ProtocolCapabilities? capabilities = null,
		string presenceClass = "play",
		bool isSecure = false,
		string? sessionId = null,
		CancellationToken cancellationToken = default);

	void RestoreDormant(ConnectionStateData data, Func<byte[], ValueTask> output, Action disconnect);

	Task DisconnectAsync(long handle, CancellationToken cancellationToken = default);

	ConnectionServerService.ConnectionData? Get(long handle);

	IEnumerable<ConnectionServerService.ConnectionData> GetAll();

	bool UpdatePreferences(long handle, SharpMUSH.ConnectionServer.Models.PlayerOutputPreferences preferences);

	bool ClearPreferences(long handle);

	bool UpdateColorStyle(long handle, string? style);

	bool UpdateCapabilities(long handle,
		Func<SharpMUSH.ConnectionServer.Models.ProtocolCapabilities,
			SharpMUSH.ConnectionServer.Models.ProtocolCapabilities> change);
}
