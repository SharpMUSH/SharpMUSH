using System.Collections.Concurrent;
using System.Text;
using MassTransit;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>
/// Manages active connections in the ConnectionServer
/// </summary>
public class ConnectionServerService(ILogger<ConnectionServerService> logger, IBus publishEndpoint) : IConnectionServerService
{
	private readonly ConcurrentDictionary<long, ConnectionData> _sessionState = [];

	public async Task RegisterAsync(
		long handle,
		string ipAddress,
		string hostname,
		string connectionType,
		Func<byte[], ValueTask> outputFunction,
		Func<byte[], ValueTask> promptOutputFunction,
		Func<Encoding> encodingFunction)
	{
		try
		{
			var data = new ConnectionData(
				handle,
				null,
				ConnectionState.Connected,
				outputFunction,
				promptOutputFunction,
				encodingFunction);

			_sessionState.AddOrUpdate(handle, data, (_, _) =>
				throw new InvalidOperationException("Handle already registered"));

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
			// Publish connection closed message to MainProcess
			await publishEndpoint.Publish(new ConnectionClosedMessage(
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
		Func<Encoding> EncodingFunction);

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
		Func<Encoding> encodingFunction);
	
	Task DisconnectAsync(long handle);

	ConnectionServerService.ConnectionData? Get(long handle);

	IEnumerable<ConnectionServerService.ConnectionData> GetAll();
}
