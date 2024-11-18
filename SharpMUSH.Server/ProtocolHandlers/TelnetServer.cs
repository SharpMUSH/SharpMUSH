using System.IO.Pipelines;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;
using TelnetNegotiationCore.Handlers;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;

namespace SharpMUSH.Server.ProtocolHandlers;

public class TelnetServer : ConnectionHandler
{
	private readonly ILogger _logger;
	private readonly IConnectionService _connectionService;
	private readonly IPublisher _publisher;
	private readonly MSSPConfig _msspConfig = new() { Name = "SharpMUSH", UTF_8 = true };

	public TelnetServer(ILogger<TelnetServer> logger, ISharpDatabase database, IConnectionService connectionService, IPublisher publisher)
	{
		Console.OutputEncoding = Encoding.UTF8;
		_logger = logger;
		_connectionService = connectionService;
		_publisher = publisher;

		// TODO: This does not belong here. A 'main thread' is needed to migrate this, before allowing telnet connections.
		_logger.LogInformation("Starting Database");
		database.Migrate().AsTask().Wait();
	}

	public override async Task OnConnectedAsync(ConnectionContext connection)
	{
		var ct = connection.ConnectionClosed;
		var MSDPHandler = new MSDPServerHandler(new MSDPServerModel(async resetVar
			=> await _publisher.Publish(
				new UpdateMSDPRequest(connection.ConnectionId, resetVar), ct)));

		var telnet = await new TelnetInterpreter(TelnetInterpreter.TelnetMode.Server, _logger)
			{
				CallbackOnSubmitAsync = async (byteArray, encoding, _)
					=> await _publisher.Publish(
						new TelnetInputRequest(connection.ConnectionId, encoding.GetString(byteArray)), ct),
				SignalOnGMCPAsync = async moduleAndInfo
					=> await _publisher.Publish(
						new SignalGMCPRequest(connection.ConnectionId, moduleAndInfo.Package, moduleAndInfo.Info), ct),
				SignalOnMSSPAsync = async msspConfig
					=> await _publisher.Publish(
						new UpdateMSSPRequest(connection.ConnectionId, msspConfig), ct),
				SignalOnNAWSAsync = async (newHeight, newWidth)
					=> await _publisher.Publish(
						new UpdateNAWSRequest(connection.ConnectionId, newHeight, newWidth), ct),
				SignalOnMSDPAsync = MSDPHandler.HandleAsync,
				CallbackNegotiationAsync = async byteArray =>
				{
					try { await connection.Transport.Output.WriteAsync(byteArray, ct); }
					catch (ObjectDisposedException ode) { _logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId); }
					catch (Exception ex) { _logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId); }
				},
				CharsetOrder = [Encoding.GetEncoding("utf-8"), Encoding.GetEncoding("iso-8859-1")]
			}
			.RegisterMSSPConfig(() => _msspConfig)
			.BuildAsync();

		_connectionService.Register(connection.ConnectionId, telnet.SendAsync, () => telnet.CurrentEncoding);

		try
		{
			while (!connection.ConnectionClosed.IsCancellationRequested)
			{
				var result = await connection.Transport.Input.ReadAsync(connection.ConnectionClosed);

				foreach (var segment in result.Buffer)
				{
					await telnet.InterpretByteArrayAsync([.. segment.Span]);
				}

				if (result.IsCompleted) break;
					
				connection.Transport.Input.AdvanceTo(result.Buffer.End);
			}
		}
		catch (ConnectionResetException) { /* Disconnected while evaluating. That's fine. It just means someone closed their client. */ }
		catch (Exception) { _logger.LogDebug("Connection {ConnectionId} disconnected unexpectedly.", connection.ConnectionId); }

		_connectionService.Disconnect(connection.ConnectionId);
	}
}