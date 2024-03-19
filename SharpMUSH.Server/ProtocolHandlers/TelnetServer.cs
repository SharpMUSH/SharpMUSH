using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database;
using SharpMUSH.Library.Services;
using System.Collections.Immutable;
using System.IO.Pipelines;
using System.Text;
using TelnetNegotiationCore.Handlers;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Models;

namespace SharpMUSH.Server.ProtocolHandlers
{
	public class TelnetServer : ConnectionHandler
	{
		private readonly ILogger _logger;
		private readonly IConnectionService _connectionService;
		private readonly MSSPConfig msspConfig = new() { Name = "SharpMUSH", UTF_8 = true };

		public TelnetServer(ILogger<TelnetServer> logger, ISharpDatabase database, IConnectionService connectionService) : base()
		{
			Console.OutputEncoding = Encoding.UTF8;
			_logger = logger;
			_connectionService = connectionService;

			// TODO: This does not belong this. A 'main thread' is needed to migrate this.
			_logger.LogInformation("Starting Database");
			database.Migrate();
		}

		private async Task WriteToOutputStreamAsync(byte[] arg, PipeWriter writer, CancellationToken ct)
		{
			try { await writer.WriteAsync(new ReadOnlyMemory<byte>(arg), ct); }
			catch (ObjectDisposedException ode) { _logger.LogError(ode, "Stream has been closed"); }
		}

		public Task SignalGMCPAsync((string module, string writeback) val)
		{
			_logger.LogDebug("GMCP Signal: {Module}: {WriteBack}", val.module, val.writeback);
			return Task.CompletedTask;
		}

		public Task SignalMSSPAsync(MSSPConfig val)
		{
			_logger.LogDebug("New MSSP: {@MSSPConfig}", val);
			return Task.CompletedTask;
		}

		public Task SignalNAWSAsync(int height, int width)
		{
			_logger.LogDebug("Client Height and Width updated: {Height}x{Width}", height, width);
			return Task.CompletedTask;
		}

		private static async Task SignalMSDPAsync(MSDPServerHandler handler, TelnetInterpreter telnet, string config) =>
			await handler.HandleAsync(telnet, config);

		public static async Task WriteBackAsync(byte[] writeback, Encoding encoding, TelnetInterpreter telnet)
		{
			var str = encoding.GetString(writeback);

			// TODO: Send to the Command Parser!
			if (str.StartsWith("echo"))
			{
				await telnet.SendAsync(encoding.GetBytes($"We heard: {str}" + Environment.NewLine));
			}
			Console.WriteLine(encoding.GetString(writeback));
		}

		private async Task MSDPUpdateBehavior(string resetVariable)
		{
			_logger.LogDebug("MSDP Reset Request: {@Reset}", resetVariable);
			await Task.CompletedTask;
		}

		public async override Task OnConnectedAsync(ConnectionContext connection)
		{
			using (_logger.BeginScope(new Dictionary<string, object> { { "ConnectionId", connection.ConnectionId } }))
			{
				_logger.LogInformation("{ConnectionId} connected", connection.ConnectionId);
				_connectionService.Register(connection.ConnectionId);

				var MSDPHandler = new MSDPServerHandler(new MSDPServerModel(MSDPUpdateBehavior) { });

				var telnet = await new TelnetInterpreter(TelnetInterpreter.TelnetMode.Server, _logger)
				{
					CallbackOnSubmitAsync = WriteBackAsync,
					SignalOnGMCPAsync = SignalGMCPAsync,
					SignalOnMSSPAsync = SignalMSSPAsync,
					SignalOnNAWSAsync = SignalNAWSAsync,
					SignalOnMSDPAsync = (telnet, config) => SignalMSDPAsync(MSDPHandler, telnet, config),
					CallbackNegotiationAsync = (x) => WriteToOutputStreamAsync(x, connection.Transport.Output, connection.ConnectionClosed),
					CharsetOrder = new[] { Encoding.GetEncoding("utf-8"), Encoding.GetEncoding("iso-8859-1") }
				}.RegisterMSSPConfig(() => msspConfig).BuildAsync();

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
				catch (Exception)
				{
					_logger.LogDebug("Connection {ConnectionId} disconnected unexpectedly.", connection.ConnectionId);
					_connectionService.Disconnect(connection.ConnectionId);
				}

				_logger.LogInformation("{ConnectionId} disconnected", connection.ConnectionId);
				_connectionService.Disconnect(connection.ConnectionId);
			}
		}
	}
}