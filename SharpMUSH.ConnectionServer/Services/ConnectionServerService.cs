using System.Collections.Concurrent;
using System.Text;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using SharpMUSH.ConnectionServer.Models;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Manages active connections in the ConnectionServer
/// </summary>
public class ConnectionServerService(
	ILogger<ConnectionServerService> logger, 
	IMessageBus publishEndpoint,
	IConnectionStateStore? stateStore = null) : IConnectionServerService
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
		ProtocolCapabilities? capabilities = null)
	{
		try
		{
			var data = new ConnectionData(
				handle,
				null,
				ConnectionState.Connected,
				outputFunction,
				promptOutputFunction,
				encodingFunction,
				disconnectFunction,
				gmcpFunction,
				capabilities ?? new ProtocolCapabilities(),
				null);

			_sessionState.AddOrUpdate(handle, data, (_, _) =>
				throw new InvalidOperationException("Handle already registered"));

			// Store in Redis if available
			if (stateStore != null)
			{
				await stateStore.SetConnectionAsync(handle, new ConnectionStateData
				{
					Handle = handle,
					PlayerRef = null,
					State = "Connected",
					IpAddress = ipAddress,
					Hostname = hostname,
					ConnectionType = connectionType,
					ConnectedAt = DateTimeOffset.UtcNow,
					LastSeen = DateTimeOffset.UtcNow,
					Metadata = new Dictionary<string, string>
					{
						{ "ConnectionStartTime", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
						{ "LastConnectionSignal", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
						{ "InternetProtocolAddress", ipAddress },
						{ "HostName", hostname },
						{ "ConnectionType", connectionType }
					}
				});
			}

			// Publish connection established message to MainProcess
			await publishEndpoint.Publish(new ConnectionEstablishedMessage(
				handle,
				ipAddress,
				hostname,
				connectionType,
				DateTimeOffset.UtcNow
			));
		}
		catch(Exception ex)
		{
			logger.LogError(ex, "Error registering connection handle: {Handle}", handle);
			await outputFunction(Encoding.UTF8.GetBytes(ex.ToString()));
		}
	}

	public async Task DisconnectAsync(long handle)
	{
		if (_sessionState.TryRemove(handle, out var data))
		{
			// Remove from Redis if available
			if (stateStore != null)
			{
				await stateStore.RemoveConnectionAsync(handle);
			}

			// Publish connection closed message to MainProcess
			await publishEndpoint.Publish(new ConnectionClosedMessage(
				handle,
				DateTimeOffset.UtcNow
			));
		}

		data?.DisconnectFunction();
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
			// TryUpdate returns false if the value changed between TryGetValue and TryUpdate
			// In that case, retry the update
			while (!_sessionState.TryUpdate(handle, updated, connection))
			{
				// Connection was updated by another thread, get the latest value and retry
				if (!_sessionState.TryGetValue(handle, out connection))
				{
					// Connection was removed
					return false;
				}
				updated = connection with { Preferences = preferences };
			}
			return true;
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
		PlayerOutputPreferences? Preferences);

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
		SharpMUSH.ConnectionServer.Models.ProtocolCapabilities? capabilities = null);
	
	Task DisconnectAsync(long handle);

	ConnectionServerService.ConnectionData? Get(long handle);

	IEnumerable<ConnectionServerService.ConnectionData> GetAll();

	bool UpdatePreferences(long handle, SharpMUSH.ConnectionServer.Models.PlayerOutputPreferences preferences);
}
