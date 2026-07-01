using System.Text;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Owns the shared connection lifecycle: register the handle with its output delegate,
/// pump inbound frames (routing browser control frames vs commands), and disconnect on close.
/// Transport-agnostic — used by both WebSocket and WebTransport handlers, so QUIC connection
/// migration underneath a WebTransport session is invisible to this loop.
/// </summary>
public sealed class ConnectionPump(
	ILogger<ConnectionPump> logger,
	IConnectionServerService connectionService,
	IMessageBus publishEndpoint,
	IDescriptorGeneratorService descriptorGenerator)
{
	public async Task RunAsync(IDuplexTransport transport, long handle, CancellationToken ct)
	{
		await connectionService.RegisterAsync(
			handle,
			transport.RemoteIp,
			transport.Hostname,
			transport.Kind,
			data => new ValueTask(transport.SendAsync(data, ct)),
			data => new ValueTask(transport.SendAsync(data, ct)),
			() => Encoding.UTF8,
			() => _ = transport.CloseAsync());

		try
		{
			while (!ct.IsCancellationRequested)
			{
				var message = await transport.ReceiveTextAsync(ct);
				if (message is null) break; // peer closed

				if (message.Length == 0) continue;

				// Browser-sent JSON control frames are handled here and NOT forwarded as commands.
				// NAWS reuses the same NAWSUpdateMessage path telnet uses (Height=rows, Width=cols).
				if (WebSocketControlFrame.TryParseNaws(message, out var cols, out var rows))
					await publishEndpoint.Publish(new NAWSUpdateMessage(handle, rows, cols), ct);
				else
					await publishEndpoint.Publish(new WebSocketInputMessage(handle, message), ct);
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown / RequestAborted.
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error pumping {Kind} connection {Handle}", transport.Kind, handle);
		}
		finally
		{
			await connectionService.DisconnectAsync(handle);
			descriptorGenerator.ReleaseWebSocketDescriptor(handle);
		}
	}
}
