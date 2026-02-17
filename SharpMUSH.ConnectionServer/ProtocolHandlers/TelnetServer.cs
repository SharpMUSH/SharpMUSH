using System.Collections.Concurrent;
using System.Net;
using System.Text;
using SharpMUSH.Messaging.Abstractions;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Handlers;
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
	private readonly ITelemetryService? _telemetryService;
	private readonly MSSPConfig _msspConfig = new() { Name = "SharpMUSH", UTF_8 = true };
	private readonly SemaphoreSlim _semaphoreSlimForWriter = new(1, 1);

	public TelnetServer(
		ILogger<TelnetServer> logger,
		IConnectionServerService connectionService,
		IMessageBus publishEndpoint,
		IDescriptorGeneratorService descriptorGenerator,
		ITelemetryService? telemetryService = null)
	{
		Console.OutputEncoding = Encoding.UTF8;
		_logger = logger;
		_connectionService = connectionService;
		_publishEndpoint = publishEndpoint;
		_descriptorGenerator = descriptorGenerator;
		_telemetryService = telemetryService;
	}

	public override async Task OnConnectedAsync(ConnectionContext connection)
	{
		var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
		var stageStopwatch = System.Diagnostics.Stopwatch.StartNew();
		
		// Stage 1: Descriptor allocation
		var nextPort = _descriptorGenerator.GetNextTelnetDescriptor();
		var ct = connection.ConnectionClosed;
		
		_telemetryService?.RecordConnectionTiming("descriptor_allocation", stageStopwatch.Elapsed.TotalMilliseconds);
		stageStopwatch.Restart();

		// Stage 2: Telnet protocol setup
		var telnet = await new TelnetInterpreterBuilder()
			.UseMode(TelnetInterpreter.TelnetMode.Server)
			.UseLogger(_logger)
		.OnNegotiation(async (data) =>
		{
			// Write telnet negotiation bytes to the network
			await connection.Transport.Output.WriteAsync(data, ct);
		})
			.OnSubmit(async (byteArray, encoding, _) =>
				await _publishEndpoint.Publish(new TelnetInputMessage(nextPort, encoding.GetString(byteArray)), ct))
			.AddPlugin<GMCPProtocol>().OnGMCPMessage(async data =>
				await _publishEndpoint.Publish(new GMCPSignalMessage(nextPort, data.Package, data.Info), ct))
			.AddPlugin<MSSPProtocol>().WithMSSPConfig(() => _msspConfig).OnMSSP(async _ =>
				// Not Yet Implemented. Need to turn config into a dictionary
				await _publishEndpoint.Publish(new MSSPUpdateMessage(nextPort, []), ct))
			.AddPlugin<NAWSProtocol>().OnNAWS(async (newHeight, newWidth) =>
				await _publishEndpoint.Publish(new NAWSUpdateMessage(nextPort, newHeight, newWidth), ct))
			.AddPlugin<MSDPProtocol>().OnMSDPMessage(MSDPCallback(connection, ct))
			.AddPlugin<CharsetProtocol>().WithCharsetOrder(Encoding.GetEncoding("utf-8"), Encoding.GetEncoding("iso-8859-1"))
			.AddPlugin<MCCPProtocol>()
			.BuildAsync();

		_telemetryService?.RecordConnectionTiming("telnet_setup", stageStopwatch.Elapsed.TotalMilliseconds);
		stageStopwatch.Restart();

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
			// Write output to the network transport with proper telnet escaping
			// TelnetSafeBytes escapes IAC (0xFF) characters and applies MCCP compression if negotiated
			_logger.LogDebug("OutputFunction called with {ByteCount} bytes for handle {Handle}", data.Length, nextPort);
			try
			{
				await _semaphoreSlimForWriter.WaitAsync(ct);
				try
				{
					var telnetSafeData = telnet.TelnetSafeBytes(data);
					await connection.Transport.Output.WriteAsync(telnetSafeData.AsMemory(), ct);
					_logger.LogDebug("Successfully wrote {ByteCount} telnet-safe bytes ({Original} original) to transport for handle {Handle}", 
						telnetSafeData.Length, data.Length, nextPort);
				}
				finally
				{
					_semaphoreSlimForWriter.Release();
				}
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
			// Write prompt output to the network transport with proper telnet escaping
			// TelnetSafeBytes escapes IAC (0xFF) characters and applies MCCP compression if negotiated
			try
			{
				await _semaphoreSlimForWriter.WaitAsync(ct);
				try
				{
					var telnetSafeData = telnet.TelnetSafeBytes(data);
					await connection.Transport.Output.WriteAsync(telnetSafeData.AsMemory(), ct);
				}
				finally
				{
					_semaphoreSlimForWriter.Release();
				}
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

	_telemetryService?.RecordConnectionTiming("connection_registration", stageStopwatch.Elapsed.TotalMilliseconds);
	_telemetryService?.RecordConnectionTiming("total_connection_establishment", totalStopwatch.Elapsed.TotalMilliseconds);

	try
	{
		while (!ct.IsCancellationRequested)
		{
			var result = await connection.Transport.Input.ReadAsync(ct);

			foreach (var segment in result.Buffer)
			{
				await telnet.InterpretByteArrayAsync(segment);
			}

			if (result.IsCompleted) break;
			connection.Transport.Input.AdvanceTo(result.Buffer.End, result.Buffer.End);
		}
	}
	catch (ConnectionResetException)
	{
		/* Disconnected while evaluating. That's fine. It just means someone closed their client. */
	}
	catch (Exception ex)
	{
		_logger.LogDebug(ex, "Connection {ConnectionId} disconnected unexpectedly.", connection.ConnectionId);
	}

	// Disconnect and notify MainProcess
	var disconnectStopwatch = System.Diagnostics.Stopwatch.StartNew();
	await _connectionService.DisconnectAsync(nextPort);
	_telemetryService?.RecordConnectionTiming("disconnection", disconnectStopwatch.Elapsed.TotalMilliseconds);
}

private Func<TelnetInterpreter, string, ValueTask> MSDPCallback(ConnectionContext connection, CancellationToken ct)
{
	/*
	var MSDPHandler = new MSDPServerHandler(new MSDPServerModel(async resetVar =>
	{
		// Publish MSDP update to MainProcess
		// resetVar is a string representing the variable name that was reset
		// Create a dictionary with the reset variable
		var msdpData = new Dictionary<string, string> { { "RESET", resetVar } };
		await _publishEndpoint.Publish(new MSDPUpdateMessage(nextPort, msdpData), ct);
	}));
	*/

	return async (ti, str) =>
	{
		try
		{
			await _semaphoreSlimForWriter.WaitAsync(ct);
			try
			{
				await connection.Transport.Output.WriteAsync(ti.TelnetSafeString(str), ct);
			}
			finally
			{
				_semaphoreSlimForWriter.Release();
			}
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