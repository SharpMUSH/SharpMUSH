using KafkaFlow;
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
/// - BytesSum distribution strategy ensures messages with same partition key go to same worker
/// - Multiple workers process different connections in parallel (no ordering constraint between connections)
/// - Grouping preserves message order within each Handle
/// - Concatenation maintains the original message sequence
/// - Different connections are processed in parallel (independent, no ordering constraint)
/// 
/// Performance:
/// - WorkerCount defaults to Environment.ProcessorCount for parallel processing
/// - BytesSum distribution maintains ordering while utilizing multiple cores
/// - Different connections processed by different workers simultaneously
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

		logger.LogTrace("[KAFKA-BATCH] TelnetOutputBatchMiddleware invoked - BatchSize: {BatchSize}",
			batch?.Count ?? 0);

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

		logger.LogTrace("[KAFKA-BATCH] Processing batch - TotalMessages: {TotalMessages}, UniqueHandles: {UniqueHandles}, Handles: [{Handles}]",
			batch.Count, messagesByHandle.Count, string.Join(", ", messagesByHandle.Select(g => g.Handle)));

		// Process each connection's messages in parallel (connections are independent)
		// This improves performance without breaking ordering guarantees
		await Parallel.ForEachAsync(messagesByHandle,
			new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
			async (group, _) =>
		{
			var handle = group.Handle;
			var connection = connectionService.Get(handle);

			logger.LogTrace("[KAFKA-BATCH] Processing handle batch - Handle: {Handle}, MessageCount: {MessageCount}, Connection: {ConnectionStatus}",
				handle, group.Messages.Count, connection != null ? "Found" : "Missing");

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

				// Use Span<byte> for efficient zero-copy concatenation
				var concatenated = new byte[totalSize];
				var destination = concatenated.AsSpan();

				// CRITICAL: Maintain order when concatenating
				// Span<byte>.CopyTo() uses optimized SIMD instructions and has better JIT inlining
				// compared to Array.Copy(), resulting in faster execution and lower CPU usage
				foreach (var msg in messages)
				{
					msg.Data.AsSpan().CopyTo(destination);
					destination = destination.Slice(msg.Data.Length);
				}

				// Transform and send the batched output
				var transformedData = await transformService.TransformAsync(
					concatenated,
					connection.Capabilities,
					connection.Preferences);

				await connection.OutputFunction(transformedData);
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Error sending batched output to connection {Handle}", handle);
			}
		});
	}
}
