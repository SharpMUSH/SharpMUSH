using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Text;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes WebSocket output messages from NATS JetStream and sends to connections.
/// The payload is an out-of-band JSON envelope (e.g. <c>{ "type": "html", ... }</c>) that the
/// browser parses itself, so it is forwarded verbatim — running it through the ANSI/charset
/// <see cref="IOutputTransformService"/> would corrupt the JSON.
/// </summary>
public class WebSocketOutputConsumer(
	IConnectionServerService connectionService,
	ILogger<WebSocketOutputConsumer> logger)
: IMessageConsumer<WebSocketOutputMessage>
{
	public async Task HandleAsync(WebSocketOutputMessage message, CancellationToken cancellationToken = default)
	{
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received WebSocket output for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			await connection.OutputFunction(Encoding.UTF8.GetBytes(message.Data));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending WebSocket output to connection {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes WebSocket prompt messages from NATS JetStream and sends to connections
/// </summary>
public class WebSocketPromptConsumer(
	IConnectionServerService connectionService,
	ILogger<WebSocketPromptConsumer> logger)
: IMessageConsumer<WebSocketPromptMessage>
{
	public async Task HandleAsync(WebSocketPromptMessage message, CancellationToken cancellationToken = default)
	{
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received WebSocket prompt for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			await connection.PromptOutputFunction(Encoding.UTF8.GetBytes(message.Data));
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending WebSocket prompt to connection {Handle}", message.Handle);
		}
	}
}
