using System.Collections.Concurrent;
using System.Text;
using MassTransit;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Manages active connections in the ConnectionServer
/// </summary>
public class ConnectionService : IConnectionService
{
	private readonly ConcurrentDictionary<long, ConnectionData> _sessionState = [];
	private readonly IPublishEndpoint _publishEndpoint;

	public ConnectionService(IPublishEndpoint publishEndpoint)
	{
		_publishEndpoint = publishEndpoint;
	}

	public async Task RegisterAsync(
		long handle,
		string ipAddress,
		string hostname,
		string connectionType,
		Func<byte[], ValueTask> outputFunction,
		Func<byte[], ValueTask> promptOutputFunction,
		Func<Encoding> encodingFunction,
		ConcurrentDictionary<string, string>? metadata = null)
	{
		var data = new ConnectionData(
			handle,
			null,
			ConnectionState.Connected,
			outputFunction,
			promptOutputFunction,
			encodingFunction,
			metadata ?? new ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				{ "ConnectionStartTime", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
				{ "LastConnectionSignal", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
				{ "InternetProtocolAddress", ipAddress },
				{ "HostName", hostname },
				{ "ConnectionType", connectionType }
			})
		);

		_sessionState.AddOrUpdate(handle, data, (_, _) => 
			throw new InvalidOperationException("Handle already registered"));

		// Publish connection established message to MainProcess
		await _publishEndpoint.Publish(new ConnectionEstablishedMessage(
			handle,
			ipAddress,
			hostname,
			connectionType,
			DateTimeOffset.UtcNow
		));
	}

	public async Task DisconnectAsync(long handle)
	{
		if (_sessionState.TryRemove(handle, out var data))
		{
			// Publish connection closed message to MainProcess
			await _publishEndpoint.Publish(new ConnectionClosedMessage(
				handle,
				DateTimeOffset.UtcNow
			));
		}
	}

	public ConnectionData? Get(long handle) =>
		_sessionState.GetValueOrDefault(handle);

	public IEnumerable<ConnectionData> GetAll() =>
		_sessionState.Values;

	public record ConnectionData(
		long Handle,
		string? PlayerDbRef,
		ConnectionState State,
		Func<byte[], ValueTask> OutputFunction,
		Func<byte[], ValueTask> PromptOutputFunction,
		Func<Encoding> EncodingFunction,
		ConcurrentDictionary<string, string> Metadata
	);

	public enum ConnectionState
	{
		Connected,
		LoggedIn,
		Disconnected
	}
}

public interface IConnectionService
{
	Task RegisterAsync(long handle, string ipAddress, string hostname, string connectionType,
		Func<byte[], ValueTask> outputFunction, Func<byte[], ValueTask> promptOutputFunction,
		Func<Encoding> encodingFunction, ConcurrentDictionary<string, string>? metadata = null);

	Task DisconnectAsync(long handle);

	ConnectionService.ConnectionData? Get(long handle);

	IEnumerable<ConnectionService.ConnectionData> GetAll();
}
