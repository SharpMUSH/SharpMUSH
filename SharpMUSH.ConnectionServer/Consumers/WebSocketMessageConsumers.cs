using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Text;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes WebSocket output messages from Kafka and sends to connections
/// </summary>
public class WebSocketOutputConsumer(
	IConnectionServerService connectionService,
	IOutputTransformService transformService,
	ILogger<WebSocketOutputConsumer> logger)
: IMessageConsumer<WebSocketOutputMessage>
{
public async Task HandleAsync(WebSocketOutputMessage message, CancellationToken cancellationToken = default)
{
var connection = connectionService.Get(message.Handle);

if (connection == null)
{
logger.LogWarning("Received WebSocket output for unknown connection handle: {Handle}", message.Handle);
return;
}

try
{
var data = Encoding.UTF8.GetBytes(message.Data);
	
// Transform output based on capabilities and preferences
var transformedData = await transformService.TransformAsync(
	data,
	connection.Capabilities,
	connection.Preferences);

await connection.OutputFunction(transformedData);
}
catch (Exception ex)
{
logger.LogError(ex, "Error sending WebSocket output to connection {Handle}", message.Handle);
}
}
}

/// <summary>
/// Consumes WebSocket prompt messages from Kafka and sends to connections
/// </summary>
public class WebSocketPromptConsumer(
	IConnectionServerService connectionService,
	IOutputTransformService transformService,
	ILogger<WebSocketPromptConsumer> logger)
: IMessageConsumer<WebSocketPromptMessage>
{
public async Task HandleAsync(WebSocketPromptMessage message, CancellationToken cancellationToken = default)
{
var connection = connectionService.Get(message.Handle);

if (connection == null)
{
logger.LogWarning("Received WebSocket prompt for unknown connection handle: {Handle}", message.Handle);
return;
}

try
{
var data = Encoding.UTF8.GetBytes(message.Data);
	
// Transform output based on capabilities and preferences
var transformedData = await transformService.TransformAsync(
	data,
	connection.Capabilities,
	connection.Preferences);

await connection.PromptOutputFunction(transformedData);
}
catch (Exception ex)
{
logger.LogError(ex, "Error sending WebSocket prompt to connection {Handle}", message.Handle);
}
}
}
