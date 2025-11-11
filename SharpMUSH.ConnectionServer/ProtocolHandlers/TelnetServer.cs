using System.Collections.Concurrent;
using System.Net;
using System.Text;
using MassTransit;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using TelnetNegotiationCore.Handlers;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Handles Telnet protocol connections and publishes messages to the message queue
/// </summary>
public class TelnetServer : ConnectionHandler
{
	private readonly ILogger _logger;
	private readonly IConnectionServerService _connectionService;
	private readonly IBus _publishEndpoint;
	private readonly MSSPConfig _msspConfig = new() { Name = "SharpMUSH", UTF_8 = true };
	private readonly SemaphoreSlim _semaphoreSlimForWriter = new(1, 1);
	private readonly NextUnoccupiedNumberGenerator _descriptorGenerator = new(0);

	public TelnetServer(
		ILogger<TelnetServer> logger,
		IConnectionServerService connectionService,
		IBus publishEndpoint)
	{
		Console.OutputEncoding = Encoding.UTF8;
		_logger = logger;
		_connectionService = connectionService;
		_publishEndpoint = publishEndpoint;
	}

	public override async Task OnConnectedAsync(ConnectionContext connection)
	{
		var nextPort = _descriptorGenerator.Get().Take(1).First();
		var ct = connection.ConnectionClosed;

		var MSDPHandler = new MSDPServerHandler(new MSDPServerModel(async resetVar =>
		{
			// Publish MSDP update to MainProcess
			// resetVar is a string representing the variable name that was reset
			// Create a dictionary with the reset variable
			var msdpData = new Dictionary<string, string> { { "RESET", resetVar } };
			await _publishEndpoint.Publish(new MSDPUpdateMessage(nextPort, msdpData), ct);
		}));

		var telnet = await new TelnetInterpreter(TelnetInterpreter.TelnetMode.Server, _logger)
		{
			CallbackOnSubmitAsync = async (byteArray, encoding, _) =>
			{
				// Publish user input to MainProcess
				await _publishEndpoint.Publish(
					new TelnetInputMessage(nextPort, encoding.GetString(byteArray)), ct);
			},
			SignalOnGMCPAsync = async moduleAndInfo =>
			{
				// Publish GMCP signal to MainProcess
				await _publishEndpoint.Publish(
					new GMCPSignalMessage(nextPort, moduleAndInfo.Package, moduleAndInfo.Info), ct);
			},
			SignalOnMSSPAsync = async msspConfig =>
			{
				// Publish MSSP update to MainProcess
				var configDict = new Dictionary<string, string>();
				// Convert MSSP config to dictionary if needed
				await _publishEndpoint.Publish(
					new MSSPUpdateMessage(nextPort, configDict), ct);
			},
			SignalOnNAWSAsync = async (newHeight, newWidth) =>
			{
				// Publish NAWS update to MainProcess
				await _publishEndpoint.Publish(
					new NAWSUpdateMessage(nextPort, newHeight, newWidth), ct);
			},
			SignalOnMSDPAsync = MSDPHandler.HandleAsync,
			CallbackNegotiationAsync = async byteArray =>
			{
				try
				{
					await _semaphoreSlimForWriter.WaitAsync(ct);
					try
					{
						await connection.Transport.Output.WriteAsync(byteArray.AsMemory(), ct);
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
			CharsetOrder = [Encoding.GetEncoding("utf-8"), Encoding.GetEncoding("iso-8859-1")]
		}
		.RegisterMSSPConfig(() => _msspConfig)
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
			telnet.SendAsync,
			telnet.SendPromptAsync,
			() => telnet.CurrentEncoding);

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
		await _connectionService.DisconnectAsync(nextPort);
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
