using MassTransit;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes telnet output messages one at a time (Kafka doesn't support Batch<T> pattern).
/// NOTE: For batching optimization, we rely on TCP buffering and Nagle's algorithm,
/// or implement application-level buffering in the NotifyService.
/// </summary>
public class BatchTelnetOutputConsumer(
	IConnectionServerService connectionService,
	ILogger<BatchTelnetOutputConsumer> logger)
	: IConsumer<TelnetOutputMessage>
{
	public async Task Consume(ConsumeContext<TelnetOutputMessage> context)
	{
		var message = context.Message;
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received output message for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			await connection.OutputFunction(message.Data);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending message to connection {Handle}", message.Handle);
		}
	}
}
