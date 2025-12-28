using MassTransit;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using System.Text;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes WebSocket output messages from MainProcess and sends to connections
/// </summary>
public class WebSocketOutputConsumer(IConnectionServerService connectionService, ILogger<WebSocketOutputConsumer> logger)
	: IConsumer<WebSocketOutputMessage>
{
	public async Task Consume(ConsumeContext<WebSocketOutputMessage> context)
	{
		var message = context.Message;
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received WebSocket output for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			var data = Encoding.UTF8.GetBytes(message.Data);
			await connection.OutputFunction(data);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending WebSocket output to connection {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes WebSocket prompt messages from MainProcess and sends to connections
/// </summary>
public class WebSocketPromptConsumer(IConnectionServerService connectionService, ILogger<WebSocketPromptConsumer> logger)
	: IConsumer<WebSocketPromptMessage>
{
	public async Task Consume(ConsumeContext<WebSocketPromptMessage> context)
	{
		var message = context.Message;
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received WebSocket prompt for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			var data = Encoding.UTF8.GetBytes(message.Data);
			await connection.PromptOutputFunction(data);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending WebSocket prompt to connection {Handle}", message.Handle);
		}
	}
}
