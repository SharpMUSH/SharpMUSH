using KafkaFlow;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Batch consumer for TelnetOutputMessage that processes messages individually
/// but benefits from KafkaFlow's batching for improved throughput.
/// </summary>
public class TelnetOutputBatchHandler(
	IConnectionServerService connectionService,
	IOutputTransformService transformService,
	ILogger<TelnetOutputBatchHandler> logger)
	: IMessageHandler<TelnetOutputMessage>
{
	public async Task Handle(IMessageContext context, TelnetOutputMessage message)
	{
		// When using AddBatching with AddTypedHandlers, this handler is called
		// for each message in the batch individually, not once for the whole batch.
		// We process each message as it comes.
		
		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received output for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			var transformedData = await transformService.TransformAsync(
				message.Data,
				connection.Capabilities,
				connection.Preferences);

			await connection.OutputFunction(transformedData);
			
			logger.LogTrace("Sent output ({Size} bytes) to connection {Handle}",
				message.Data.Length, message.Handle);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending output to connection {Handle}", message.Handle);
		}
	}
}
