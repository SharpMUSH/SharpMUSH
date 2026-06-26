using Microsoft.AspNetCore.Connections;
using SharpMUSH.ConnectionServer.Configuration;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Utilities;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Net;
using System.Text;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Protocols;
using TelnetNegotiationCore.Models;

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
	private readonly ITelnetInterpreterFactory _telnetFactory;
	private readonly ConnectionServerOptions _options;
	private readonly MSSPConfig _msspConfig = new() { Name = "SharpMUSH", UTF_8 = true };

	/// <summary>
	/// The Pueblo hello string sent to clients on connect.
	/// Clients that support Pueblo respond with "PUEBLOCLIENT ...".
	/// </summary>
	private static readonly byte[] PuebloHelloBytes =
		Encoding.UTF8.GetBytes(ErrorMessages.Notifications.PuebloHello);

	public TelnetServer(
		ILogger<TelnetServer> logger,
		IConnectionServerService connectionService,
		IMessageBus publishEndpoint,
		IDescriptorGeneratorService descriptorGenerator,
		ITelnetInterpreterFactory telnetFactory,
		ConnectionServerOptions options)
	{
		Console.OutputEncoding = Encoding.UTF8;
		_logger = logger;
		_connectionService = connectionService;
		_publishEndpoint = publishEndpoint;
		_descriptorGenerator = descriptorGenerator;
		_telnetFactory = telnetFactory;
		_options = options;
	}

	public override async Task OnConnectedAsync(ConnectionContext connection)
	{
		var nextPort = _descriptorGenerator.GetNextTelnetDescriptor();
		var ct = connection.ConnectionClosed;
		var awaitingInitialPuebloHandshake = _options.PuebloEnabled;

		TelnetInterpreterBuilder builder = _telnetFactory.CreateBuilder()
			.OnSubmit(async (byteArray, encoding, _) =>
			{
				var input = encoding.GetString(byteArray);

				// Detect Pueblo handshake response only on the first submitted line.
				if (awaitingInitialPuebloHandshake &&
				    input.StartsWith("PUEBLOCLIENT", StringComparison.OrdinalIgnoreCase))
				{
					awaitingInitialPuebloHandshake = false;
					_logger.LogInformation("Pueblo handshake detected on handle {Handle}: {Response}",
						nextPort, input.TrimEnd());

					if (await TryUpdateFormatAsync(nextPort, OutputFormat.Pueblo, ct))
					{
						_logger.LogDebug("Updated Pueblo capabilities for handle {Handle}", nextPort);
					}

					await _publishEndpoint.Publish(
						new PuebloNegotiatedMessage(nextPort, input.TrimEnd()), ct);

					// Suppress this line from reaching the command parser
					return;
				}

				awaitingInitialPuebloHandshake = false;
				await _publishEndpoint.Publish(new TelnetInputMessage(nextPort, input), ct);
			})
			.AddPlugin<GMCPProtocol>().OnGMCPMessage(async data =>
				await _publishEndpoint.Publish(new GMCPSignalMessage(nextPort, data.Package, data.Info), ct))
			.AddPlugin<MSSPProtocol>().WithMSSPConfig(() => _msspConfig).OnMSSP(async _ =>
				// Not Yet Implemented. Need to turn config into a dictionary
				await _publishEndpoint.Publish(new MSSPUpdateMessage(nextPort, []), ct))
			.AddPlugin<NAWSProtocol>().OnNAWS(async (newHeight, newWidth) =>
				await _publishEndpoint.Publish(new NAWSUpdateMessage(nextPort, newHeight, newWidth), ct))
			.AddPlugin<MSDPProtocol>().OnMSDPMessage(MSDPCallback(connection))
			.AddPlugin<CharsetProtocol>().WithCharsetOrder(Encoding.GetEncoding("utf-8"), Encoding.GetEncoding("iso-8859-1"))
			.AddPlugin<MCCPProtocol>();

		if (_options.MxpEnabled)
		{
			builder = builder.AddPlugin<MXPProtocol>().OnMXPEnabled(() =>
			{
				_logger.LogInformation("MXP negotiated on handle {Handle}", nextPort);

				async ValueTask DoMxpSetup()
				{
					if (await TryUpdateFormatAsync(nextPort, OutputFormat.Mxp, ct))
					{
						_logger.LogDebug("Updated MXP capabilities for handle {Handle}", nextPort);
					}
					else if (!ct.IsCancellationRequested)
					{
						_logger.LogWarning("MXP negotiated but connection {Handle} not yet registered", nextPort);
					}

					await _publishEndpoint.Publish(new MxpNegotiatedMessage(nextPort), ct);
				}

				return DoMxpSetup();
			});
		}

		var (telnet, readTask) = await builder.BuildAndStartAsync(connection.Transport, ct);

		var remoteIp = connection.RemoteEndPoint is not IPEndPoint remoteEndpoint
			? "unknown"
			: $"{remoteEndpoint.Address}:{remoteEndpoint.Port}";

		// Send Pueblo hello before registration so the client can respond
		// before the welcome screen is sent by the main process.
		if (_options.PuebloEnabled)
		{
			await telnet.SendAsync(PuebloHelloBytes);
		}

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
			await telnet.SendGMCPCommand(module, message);
		});

		try
		{
			// Await the read task returned by BuildAndStartAsync, which completes
			// when the connection closes or the cancellation token is triggered.
			await readTask;
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

		await _connectionService.DisconnectAsync(nextPort);
		_descriptorGenerator.ReleaseTelnetDescriptor(nextPort);
	}

	private async ValueTask<bool> TryUpdateFormatAsync(long handle, OutputFormat format, CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < ConnectionRetryPolicy.MaxAttempts; attempt++)
		{
			var conn = _connectionService.Get(handle);
			if (conn != null)
			{
				return _connectionService.UpdateCapabilities(handle, conn.Capabilities with { Format = format });
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return false;
			}

			try
			{
				await Task.Delay(ConnectionRetryPolicy.Delay, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		return false;
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
