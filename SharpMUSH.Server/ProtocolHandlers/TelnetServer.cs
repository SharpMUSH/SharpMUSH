using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services;
using TelnetNegotiationCore.Handlers;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Server.ProtocolHandlers
{
	public class TelnetServer : ConnectionHandler
	{
		// TelnetOutputRequest
		private readonly ILogger _logger;
		private readonly IConnectionService _connectionService;
		private readonly IPublisher _publisher;
		private readonly MSSPConfig msspConfig = new() { Name = "SharpMUSH", UTF_8 = true };

		public TelnetServer(
				ILogger<TelnetServer> logger,
				ISharpDatabase database,
				IConnectionService connectionService,
				IPublisher publisher
		)
				: base()
		{
			Console.OutputEncoding = Encoding.UTF8;
			_logger = logger;
			_connectionService = connectionService;
			_publisher = publisher;


			// TODO: This does not belong this. A 'main thread' is needed to migrate this.
			_logger.LogInformation("Starting Database");
			database.Migrate();
		}

		private async Task WriteToOutputStreamAsync(
				byte[] arg,
				PipeWriter writer,
				string handle,
				CancellationToken ct
		)
		{
			try
			{
				await writer.WriteAsync(new ReadOnlyMemory<byte>(arg), ct);
			}
			catch (ObjectDisposedException ode)
			{
				_logger.LogError(ode, "{ConnectionId} Stream has been closed", handle);
			}
		}

		public async Task SignalGMCPAsync((string module, string writeback) val, string handle) =>
				await _publisher.Publish(new SignalGMCPRequest(handle, val.module, val.writeback));

		public async Task SignalMSSPAsync(MSSPConfig val, string handle) =>
				await _publisher.Publish(new UpdateMSSPRequest(handle, val));

		public async Task SignalNAWSAsync(int height, int width, string handle) =>
				await _publisher.Publish(new UpdateNAWSRequest(handle, height, width));

		private static async Task SignalMSDPAsync(
				MSDPServerHandler handler,
				TelnetInterpreter telnet,
				string config
		) => await handler.HandleAsync(telnet, config);

		public async Task WriteBackAsync(
				byte[] writeback,
				Encoding encoding,
				string handle
		) => await _publisher.Publish(new TelnetInputRequest(handle, encoding.GetString(writeback)));

		private async Task MSDPUpdateBehavior(string resetVariable, string handle) =>
				await _publisher.Publish(new UpdateMSDPRequest(handle, resetVariable));

		public override async Task OnConnectedAsync(ConnectionContext connection)
		{
			var MSDPHandler = new MSDPServerHandler(
					new MSDPServerModel(x => MSDPUpdateBehavior(x, connection.ConnectionId)) { }
			);

			var telnet = await new TelnetInterpreter(
					TelnetInterpreter.TelnetMode.Server,
					_logger
			)
			{
				CallbackOnSubmitAsync = (writeback, encoding, telnet_instance) =>
						WriteBackAsync(
								writeback,
								encoding,
								connection.ConnectionId
						),
				SignalOnGMCPAsync = module_and_writeback =>
						SignalGMCPAsync(module_and_writeback, connection.ConnectionId),
				SignalOnMSSPAsync = msspConfig =>
						SignalMSSPAsync(msspConfig, connection.ConnectionId),
				SignalOnNAWSAsync = (newHeight, newWidth) =>
						SignalNAWSAsync(newHeight, newWidth, connection.ConnectionId),
				SignalOnMSDPAsync = (telnet, config) =>
						SignalMSDPAsync(MSDPHandler, telnet, config),
				CallbackNegotiationAsync = (x) => WriteToOutputStreamAsync(x, connection.Transport.Output, connection.ConnectionId, connection.ConnectionClosed),
				CharsetOrder = new[]
					{
												Encoding.GetEncoding("utf-8"),
												Encoding.GetEncoding("iso-8859-1")
						}
			}.RegisterMSSPConfig(() => msspConfig)
				 .BuildAsync();

			_connectionService.Register(connection.ConnectionId, telnet.SendAsync, () => telnet.CurrentEncoding);
			// TODO: Move this to commands.
			// _connectionService.Bind(connection.ConnectionId, new DBRef(1, 1709704139507));

			try
			{
				while (!connection.ConnectionClosed.IsCancellationRequested)
				{
					var result = await connection.Transport.Input.ReadAsync(connection.ConnectionClosed);

					foreach (var segment in result.Buffer)
					{
						await telnet.InterpretByteArrayAsync(segment.Span.ToImmutableArray());
					}

					if (result.IsCompleted)
					{
						break;
					}

					connection.Transport.Input.AdvanceTo(result.Buffer.End);
				}
			}
			catch (ConnectionResetException)
			{
				// Disconnected while evaluating. That's fine. It just means someone closed their client.
			}
			catch (Exception)
			{
				_logger.LogDebug("Connection {ConnectionId} disconnected unexpectedly.", connection.ConnectionId);
			}

			_connectionService.Disconnect(connection.ConnectionId);
		}
	}
}
