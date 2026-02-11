using KafkaFlow;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Batch middleware for TelnetOutputMessage following KafkaFlow's recommended pattern.
/// Implements IMessageMiddleware to process batches using GetMessagesBatch().
/// Groups messages by Handle and concatenates data before sending to TCP connections.
/// </summary>
public class TelnetOutputBatchMiddleware : IMessageMiddleware
{
	private readonly IServiceScopeFactory _serviceScopeFactory;
	private readonly ILogger<TelnetOutputBatchMiddleware> _logger;

	public TelnetOutputBatchMiddleware(
		IServiceScopeFactory serviceScopeFactory,
		ILogger<TelnetOutputBatchMiddleware> logger)
	{
		_serviceScopeFactory = serviceScopeFactory;
		_logger = logger;
	}

	public async Task Invoke(IMessageContext context, MiddlewareDelegate next)
	{
		// Get the batch of messages as per KafkaFlow's recommended pattern
		var batch = context.GetMessagesBatch();
		
		if (batch == null || batch.Count == 0)
		{
			_logger.LogWarning("Received empty batch");
			return;
		}

		// Create a scope for dependency resolution (required by KafkaFlow)
		using var scope = _serviceScopeFactory.CreateScope();
		var connectionService = scope.ServiceProvider.GetRequiredService<IConnectionServerService>();
		var transformService = scope.ServiceProvider.GetRequiredService<IOutputTransformService>();

		// Group messages by Handle (connection ID)
		var messagesByHandle = batch
			.Select(ctx => ctx.Message.Value as TelnetOutputMessage)
			.Where(msg => msg != null)
			.GroupBy(msg => msg!.Handle)
			.ToList();

		_logger.LogDebug("Processing batch of {Count} messages for {ConnectionCount} connections",
			batch.Count, messagesByHandle.Count);

		// Process each connection's messages as a batch
		foreach (var group in messagesByHandle)
		{
			var handle = group.Key;
			var connection = connectionService.Get(handle);

			if (connection == null)
			{
				_logger.LogWarning("Received output for unknown connection handle: {Handle}", handle);
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

				_logger.LogTrace("Sent batched output ({Size} bytes from {MessageCount} messages) to connection {Handle}",
					totalSize, group.Count(), handle);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error sending batched output to connection {Handle}", handle);
			}
		}

		// Don't call next - batch processing is terminal
	}
}
