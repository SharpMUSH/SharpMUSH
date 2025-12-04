using MassTransit;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes telnet output messages and sends them to the batching service.
/// The TelnetOutputBatchingService combines multiple sequential messages into single TCP writes,
/// solving the @dolist performance issue.
/// </summary>
public class BatchTelnetOutputConsumer(
	TelnetOutputBatchingService batchingService,
	ILogger<BatchTelnetOutputConsumer> logger)
	: IConsumer<TelnetOutputMessage>
{
	public Task Consume(ConsumeContext<TelnetOutputMessage> context)
	{
		var message = context.Message;
		
		try
		{
			// Add message to batching service for efficient TCP writes
			batchingService.AddMessage(message.Handle, message.Data);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error adding message to batch for connection {Handle}", message.Handle);
		}
		
		// Return immediately - batching service handles async flushing
		return Task.CompletedTask;
	}
}
