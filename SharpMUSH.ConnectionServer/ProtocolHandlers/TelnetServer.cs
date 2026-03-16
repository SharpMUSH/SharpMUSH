using Microsoft.AspNetCore.Connections;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Net;
using System.Text;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
using TelnetNegotiationCore.Protocols;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Handles Telnet protocol connections and publishes messages to the message queue
/// </summary>
public class TelnetServer : ConnectionHandler
{
	private readonly ILogger _logger;
	private readonly IConnectionServerService _connectionService;
	private readonly IMessageBus _publishEndpoint;
	private readonly IDescriptorGeneratorService _descriptorGenerator;
	private readonly MSSPConfig _msspConfig = new() { Name = "SharpMUSH", UTF_8 = true };

	public TelnetServer(
		ILogger<TelnetServer> logger,
		IConnectionServerService connectionService,
		IMessageBus publishEndpoint,
		IDescriptorGeneratorService descriptorGenerator)
	{
		Console.OutputEncoding = Encoding.UTF8;
		_logger = logger;
		_connectionService = connectionService;
		_publishEndpoint = publishEndpoint;
		_descriptorGenerator = descriptorGenerator;
	}

	public override async Task OnConnectedAsync(ConnectionContext connection)
	{
		var nextPort = _descriptorGenerator.GetNextTelnetDescriptor();
		var ct = connection.ConnectionClosed;

		var telnet = await new TelnetInterpreterBuilder()
			.UseMode(TelnetInterpreter.TelnetMode.Server)
			.UseLogger(_logger)
			.UsePipe(connection.Transport)
			.OnSubmit(async (byteArray, encoding, _) =>
				await _publishEndpoint.Publish(new TelnetInputMessage(nextPort, encoding.GetString(byteArray)), ct))
			.AddPlugin<GMCPProtocol>().OnGMCPMessage(async data =>
				await _publishEndpoint.Publish(new GMCPSignalMessage(nextPort, data.Package, data.Info), ct))
			.AddPlugin<MSSPProtocol>().WithMSSPConfig(() => _msspConfig).OnMSSP(async _ =>
				// Not Yet Implemented. Need to turn config into a dictionary
				await _publishEndpoint.Publish(new MSSPUpdateMessage(nextPort, []), ct))
			.AddPlugin<NAWSProtocol>().OnNAWS(async (newHeight, newWidth) =>
				await _publishEndpoint.Publish(new NAWSUpdateMessage(nextPort, newHeight, newWidth), ct))
			.AddPlugin<MSDPProtocol>().OnMSDPMessage(MSDPCallback(connection))
			.AddPlugin<CharsetProtocol>().WithCharsetOrder(Encoding.GetEncoding("utf-8"), Encoding.GetEncoding("iso-8859-1"))
			.AddPlugin<MCCPProtocol>()
			.BuildAsync();

		var remoteIp = connection.RemoteEndPoint is not IPEndPoint remoteEndpoint
			? "unknown"
			: $"{remoteEndpoint.Address}:{remoteEndpoint.Port}";

		// Register connection in ConnectionService
		await _connectionService.RegisterAsync(
			nextPort,
			remoteIp,
			connection.RemoteEndPoint?.ToString() ?? remoteIp,
			"telnet",
		async (data) =>
		{
			// Write output to the network transport using SendAsync which handles
			// IAC (0xFF) escaping and CRLF line termination internally.
			// Write serialization is handled by the library's internal write lock.
			_logger.LogTrace("OutputFunction called with {ByteCount} bytes for handle {Handle}", data.Length, nextPort);
			try
			{
				await telnet.SendAsync(data);
				_logger.LogTrace("Successfully sent {ByteCount} bytes to transport for handle {Handle}",
					data.Length, nextPort);
			}
			catch (ObjectDisposedException ode)
			{
				_logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId);
			}
		},
		async (data) =>
		{
			// Write prompt output using SendPromptAsync which handles IAC (0xFF) escaping
			// and adds the appropriate prompt terminator (EOR, GA, or CRLF) based on
			// what the client has negotiated.
			// Write serialization is handled by the library's internal write lock.
			try
			{
				await telnet.SendPromptAsync(data);
			}
			catch (ObjectDisposedException ode)
			{
				_logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId);
			}
		},
		() => telnet.CurrentEncoding,
		connection.Abort,
		async (module, message) =>
		{
			// Send GMCP message using TelnetNegotiationCore library method
			await telnet.SendGMCPCommand(module, message);
		});

		try
		{
			// Use the library's static read loop helper which handles reading from
			// the pipe input and feeding bytes to the interpreter
			await TelnetInterpreterBuilder.ReadFromPipeAsync(telnet, connection.Transport.Input, ct);
		}
		catch (ConnectionResetException)
		{
			/* Disconnected while evaluating. That's fine. It just means someone closed their client. */
		}
		catch (OperationCanceledException)
		{
			/* Connection closed via cancellation token - normal shutdown path. */
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Connection {ConnectionId} disconnected unexpectedly.", connection.ConnectionId);
		}

		// Disconnect and notify MainProcess
		await _connectionService.DisconnectAsync(nextPort);
	}

	private Func<TelnetInterpreter, string, ValueTask> MSDPCallback(ConnectionContext connection)
	{
		return async (ti, str) =>
		{
			try
			{
				// Write MSDP response using the library's thread-safe WriteToNetworkAsync
				await ti.WriteToNetworkAsync(ti.CurrentEncoding.GetBytes(str));
			}
			catch (ObjectDisposedException ode)
			{
				_logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId);
			}
		};
	}
}

/// <summary>
/// Helper class to generate unique connection descriptors
/// </summary>
public class NextUnoccupiedNumberGenerator(long start)
{
	private long _current = start;

	public IEnumerable<long> Get()
	{
		while (true)
		{
			yield return Interlocked.Increment(ref _current);
		}
	}
}