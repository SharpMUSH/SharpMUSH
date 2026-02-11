using KafkaFlow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Batch middleware for TelnetOutputMessage following KafkaFlow's recommended pattern.
/// 
/// IMPORTANT: This middleware is ONLY used for TelnetOutputMessage consumer.
/// Each KafkaFlow consumer has its own independent middleware pipeline, so this
/// batching behavior does NOT affect other message types like TelnetPromptMessage,
/// BroadcastMessage, GMCPOutputMessage, etc. Those use their own regular consumers.
/// 
/// How it works:
/// 1. Implements IMessageMiddleware to process batches (not IMessageHandler)
/// 2. Uses GetMessagesBatch() to retrieve accumulated messages
/// 3. Groups messages by Handle (connection ID) while preserving order
/// 4. Concatenates data for each connection IN ORDER
/// 5. Sends batched output to TCP connections in parallel
/// 
/// Ordering Guarantees:
/// - Messages with the same Handle use the same partition key (Handle.ToString())
/// - Kafka guarantees ordering within a partition
/// - Single worker (WithWorkersCount(1)) ensures partitions are processed in order
/// - Grouping preserves message order within each Handle
/// - Concatenation maintains the original message sequence
/// - Different connections are processed in parallel (independent, no ordering constraint)
/// 
/// Configuration: Batches up to 100 messages or waits 10ms before processing.
/// </summary>
public class TelnetOutputBatchMiddleware(
	IServiceScopeFactory serviceScopeFactory,
	ILogger<TelnetOutputBatchMiddleware> logger)
	: IMessageMiddleware
{
	public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
	{
		// Get the batch of messages as per KafkaFlow's recommended pattern
		var batch = context.GetMessagesBatch();
		
		if (batch == null || batch.Count == 0)
		{
			logger.LogWarning("Received empty batch");
			return;
		}

		// Create a scope for dependency resolution (required by KafkaFlow)
		using var scope = serviceScopeFactory.CreateScope();
		var connectionService = scope.ServiceProvider.GetRequiredService<IConnectionServerService>();
		var transformService = scope.ServiceProvider.GetRequiredService<IOutputTransformService>();

		// Group messages by Handle (connection ID) while preserving order within each group
		// Since messages with the same Handle use the same partition key, Kafka guarantees
		// they arrive in order. We must preserve this order when concatenating.
		var messagesByHandle = batch
			.Select(ctx => (TelnetOutputMessage)ctx.Message.Value)
			.GroupBy(msg => msg!.Handle, 
				msg => msg, 
				(key, msgs) => new { Handle = key, Messages = msgs.ToList() })
			.ToList();

		logger.LogDebug("Processing batch of {Count} messages for {ConnectionCount} connections",
			batch.Count, messagesByHandle.Count);

		// Process each connection's messages in parallel (connections are independent)
		// This improves performance without breaking ordering guarantees
		await Parallel.ForEachAsync(messagesByHandle, 
			new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
			async (group, _) =>
		{
			var handle = group.Handle;
			var connection = connectionService.Get(handle);

			if (connection == null)
			{
				logger.LogWarning("Received output for unknown connection handle: {Handle}", handle);
				return;
			}

			try
			{
				// Concatenate all message data for this connection IN ORDER
				var messages = group.Messages;
				var totalSize = messages.Sum(msg => msg?.Data?.Length ?? 0);
				
				if (totalSize == 0)
				{
					return; // Skip if no valid data
				}
				
				var concatenated = new byte[totalSize];
				var offset = 0;

				// CRITICAL: Maintain order when concatenating
				foreach (var msg in messages)
				{
					Array.Copy(msg.Data, 0, concatenated, offset, msg.Data.Length);
					offset += msg.Data.Length;
				}

				// Transform and send the batched output
				var transformedData = await transformService.TransformAsync(
					concatenated,
					connection.Capabilities,
					connection.Preferences);

				await connection.OutputFunction(transformedData);

				logger.LogTrace("Sent batched output ({Size} bytes from {MessageCount} messages) to connection {Handle}",
					totalSize, messages.Count, handle);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error sending batched output to connection {Handle}", handle);
			}
		});
	}
}
