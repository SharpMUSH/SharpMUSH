using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes telnet output messages from Kafka and sends to connections
/// </summary>
public class TelnetOutputConsumer(IConnectionServerService connectionService, ILogger<TelnetOutputConsumer> logger)
: IMessageConsumer<TelnetOutputMessage>
{
public async Task HandleAsync(TelnetOutputMessage message, CancellationToken cancellationToken = default)
{
var connection = connectionService.Get(message.Handle);

if (connection == null)
{
logger.LogWarning("Received output for unknown connection handle: {Handle}", message.Handle);
return;
}

try
{
await connection.OutputFunction(message.Data);
}
catch (Exception ex)
{
logger.LogError(ex, "Error sending output to connection {Handle}", message.Handle);
}
}
}

/// <summary>
/// Consumes telnet prompt messages from Kafka and sends to connections
/// </summary>
public class TelnetPromptConsumer(IConnectionServerService connectionService, ILogger<TelnetPromptConsumer> logger)
: IMessageConsumer<TelnetPromptMessage>
{
public async Task HandleAsync(TelnetPromptMessage message, CancellationToken cancellationToken = default)
{
var connection = connectionService.Get(message.Handle);

if (connection == null)
{
logger.LogWarning("Received prompt for unknown connection handle: {Handle}", message.Handle);
return;
}

try
{
await connection.PromptOutputFunction(message.Data);
}
catch (Exception ex)
{
logger.LogError(ex, "Error sending prompt to connection {Handle}", message.Handle);
}
}
}

/// <summary>
/// Consumes broadcast messages from Kafka and sends to all connections
/// </summary>
public class BroadcastConsumer(IConnectionServerService connectionService, ILogger<BroadcastConsumer> logger)
: IMessageConsumer<BroadcastMessage>
{
public async Task HandleAsync(BroadcastMessage message, CancellationToken cancellationToken = default)
{
var connections = connectionService.GetAll();

foreach (var connection in connections)
{
try
{
await connection.OutputFunction(message.Data);
}
catch (Exception ex)
{
logger.LogError(ex, "Error broadcasting to connection {Handle}", connection.Handle);
}
}
}
}

/// <summary>
/// Consumes disconnect commands from Kafka
/// </summary>
public class DisconnectConnectionConsumer(
IConnectionServerService connectionService,
ILogger<DisconnectConnectionConsumer> logger)
: IMessageConsumer<DisconnectConnectionMessage>
{
public async Task HandleAsync(DisconnectConnectionMessage message, CancellationToken cancellationToken = default)
{
logger.LogInformation("Disconnecting connection {Handle}. Reason: {Reason}", 
message.Handle, message.Reason ?? "None");

await connectionService.DisconnectAsync(message.Handle);
}
}
