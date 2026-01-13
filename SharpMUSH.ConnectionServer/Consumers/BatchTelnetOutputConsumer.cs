using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes telnet output messages from Kafka and sends them to the batching service.
/// The TelnetOutputBatchingService combines multiple sequential messages into single TCP writes,
/// solving the @dolist performance issue.
/// </summary>
public class BatchTelnetOutputConsumer(
TelnetOutputBatchingService batchingService,
ILogger<BatchTelnetOutputConsumer> logger)
: IMessageConsumer<TelnetOutputMessage>
{
public Task HandleAsync(TelnetOutputMessage message, CancellationToken cancellationToken = default)
{
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
