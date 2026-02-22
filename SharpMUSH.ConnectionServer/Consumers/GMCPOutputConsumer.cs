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
		logger.LogTrace("[KAFKA-RECV] GMCPOutputMessage received - Handle: {Handle}, Module: {Module}, MessageLength: {MessageLength}",
			message.Handle, message.Module, message.Message?.Length ?? 0);

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

		if (message.Message is null)
		{
			logger.LogWarning("Connection {Handle} received GMCP module {Module} with null message body; skipping dispatch.", message.Handle, message.Module);
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
