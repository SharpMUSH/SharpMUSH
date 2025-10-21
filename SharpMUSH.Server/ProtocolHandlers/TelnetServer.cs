using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Mediator;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Notifications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
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
	private readonly SemaphoreSlim _semaphoreSlimForWriter = new(1, 1);
	private readonly NextUnoccupiedNumberGenerator _descriptorGenerator = new(0);

	public TelnetServer(ILogger<TelnetServer> logger, IConnectionService connectionService, IPublisher publisher)
	{
		Console.OutputEncoding = Encoding.UTF8;
		_logger = logger;
		_connectionService = connectionService;
		_publisher = publisher;
	}

	/// <summary>
	/// TODO: Something in this sector is holding up the thread when the console is being quit.
	/// It's likely that the Connection.Closed is not the same as the Console.Closed.
	/// </summary>
	/// <param name="connection"></param>
	public override async Task OnConnectedAsync(ConnectionContext connection)
	{
		var nextPort = _descriptorGenerator.Get().Take(1).First();
		var ct = connection.ConnectionClosed;
		var MSDPHandler = new MSDPServerHandler(new MSDPServerModel(async resetVar
			=> await _publisher.Publish(
				new UpdateMSDPNotification(nextPort, resetVar), ct)));

		var telnet = await new TelnetInterpreter(TelnetInterpreter.TelnetMode.Server, _logger)
			{
				CallbackOnSubmitAsync = async (byteArray, encoding, _)
					=> await _publisher.Publish(
						new TelnetInputNotification(nextPort, encoding.GetString(byteArray)), ct),
				SignalOnGMCPAsync = async moduleAndInfo
					=> await _publisher.Publish(
						new SignalGMCPNotification(nextPort, moduleAndInfo.Package, moduleAndInfo.Info), ct),
				SignalOnMSSPAsync = async msspConfig
					=> await _publisher.Publish(
						new UpdateMSSPNotification(nextPort, msspConfig), ct),
				SignalOnNAWSAsync = async (newHeight, newWidth)
					=> await _publisher.Publish(
						new UpdateNAWSNotification(nextPort, newHeight, newWidth), ct),
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

		_connectionService.Register(nextPort,
			remoteIp,
			connection.RemoteEndPoint?.ToString() ?? remoteIp,
			"telnet",
			telnet.SendAsync,
			telnet.SendPromptAsync,
			() => telnet.CurrentEncoding,
			new ConcurrentDictionary<string, string>(new Dictionary<string, string>()));

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
		catch (Exception)
		{
			_logger.LogDebug("Connection {ConnectionId} disconnected unexpectedly.", connection.ConnectionId);
		}

		_connectionService.Disconnect(nextPort);
	}
}