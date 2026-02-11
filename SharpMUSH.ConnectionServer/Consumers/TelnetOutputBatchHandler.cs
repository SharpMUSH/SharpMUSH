using KafkaFlow;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Batch consumer for TelnetOutputMessage that concatenates messages per connection
/// and sends them as batched TCP output for improved performance.
/// Uses KafkaFlow's batch consume middleware.
/// </summary>
public class TelnetOutputBatchHandler(
	IConnectionServerService connectionService,
	IOutputTransformService transformService,
	ILogger<TelnetOutputBatchHandler> logger)
	: IMessageHandler<TelnetOutputMessage>
{
	public async Task Handle(IMessageContext context, TelnetOutputMessage message)
	{
		// Get the batch of messages from the context
		var batch = context.GetMessagesBatch();
		
		if (batch == null || batch.Count == 0)
		{
			logger.LogWarning("Received empty batch");
			return;
		}

		// Group messages by Handle (connection ID)
		// In a batch, context.Message contains the deserialized message
		var messagesByHandle = batch
			.Select(ctx => ctx.Message.Value as TelnetOutputMessage)
			.Where(msg => msg != null)
			.GroupBy(msg => msg!.Handle)
			.ToList();

		logger.LogDebug("Processing batch of {Count} messages for {ConnectionCount} connections",
			batch.Count, messagesByHandle.Count);

		// Process each connection's messages
		foreach (var group in messagesByHandle)
		{
			var handle = group.Key;
			var connection = connectionService.Get(handle);

			if (connection == null)
			{
				logger.LogWarning("Received output for unknown connection handle: {Handle}", handle);
				continue;
			}

			try
			{
				// Concatenate all message data for this connection
				var messages = group.ToList();
				var totalSize = messages.Sum(msg => msg?.Data?.Length ?? 0);
				
				if (totalSize == 0)
				{
					continue; // Skip if no valid data
				}
				
				var concatenated = new byte[totalSize];
				var offset = 0;

				foreach (var msg in messages)
				{
					if (msg?.Data != null)
					{
						Array.Copy(msg.Data, 0, concatenated, offset, msg.Data.Length);
						offset += msg.Data.Length;
					}
				}

				// Transform and send the batched output
				var transformedData = await transformService.TransformAsync(
					concatenated,
					connection.Capabilities,
					connection.Preferences);

				await connection.OutputFunction(transformedData);

				logger.LogTrace("Sent batched output ({Size} bytes from {MessageCount} messages) to connection {Handle}",
					totalSize, group.Count(), handle);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error sending batched output to connection {Handle}", handle);
			}
		}
	}
}
