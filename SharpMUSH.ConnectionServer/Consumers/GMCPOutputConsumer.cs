using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes GMCP output messages from Kafka and sends to connections
/// </summary>
public class GMCPOutputConsumer(IConnectionServerService connectionService, ILogger<GMCPOutputConsumer> logger)
: IMessageConsumer<GMCPOutputMessage>
{
public async Task HandleAsync(GMCPOutputMessage message, CancellationToken cancellationToken = default)
{
var connection = connectionService.Get(message.Handle);

if (connection == null)
{
logger.LogWarning("Received GMCP output for unknown connection handle: {Handle}", message.Handle);
return;
}

if (connection.GMCPFunction == null)
{
logger.LogWarning("Connection {Handle} does not support GMCP", message.Handle);
return;
}

try
{
await connection.GMCPFunction(message.Module, message.Message);
}
catch (Exception ex)
{
logger.LogError(ex, "Error sending GMCP to connection {Handle}", message.Handle);
}
}
}
