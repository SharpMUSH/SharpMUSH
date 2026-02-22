using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes telnet prompt messages from Kafka and sends to connections
/// </summary>
public class TelnetPromptConsumer(
	IConnectionServerService connectionService,
	IOutputTransformService transformService,
	ILogger<TelnetPromptConsumer> logger)
	: IMessageConsumer<TelnetPromptMessage>
{
	public async Task HandleAsync(TelnetPromptMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[KAFKA-RECV] TelnetPromptMessage received - Handle: {Handle}, DataLength: {DataLength}",
			message.Handle, message.Data?.Length ?? 0);

		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received prompt for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			if (message.Data is null)
			{
				logger.LogWarning("Received TelnetPromptMessage with null data for handle: {Handle}", message.Handle);
				return;
			}

			var transformedData = await transformService.TransformAsync(
				message.Data,
				connection.Capabilities,
				connection.Preferences);

			await connection.PromptOutputFunction(transformedData);
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
public class BroadcastConsumer(
	IConnectionServerService connectionService,
	IOutputTransformService transformService,
	ILogger<BroadcastConsumer> logger)
	: IMessageConsumer<BroadcastMessage>
{
	public async Task HandleAsync(BroadcastMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[KAFKA-RECV] BroadcastMessage received - DataLength: {DataLength}",
			message.Data?.Length ?? 0);

		var connections = connectionService.GetAll();

		if (message.Data is null)
		{
			logger.LogWarning("Received BroadcastMessage with null data");
			return;
		}

		foreach (var connection in connections)
		{
			try
			{
				// Transform output based on capabilities and preferences
				var transformedData = await transformService.TransformAsync(
					message.Data,
					connection.Capabilities,
					connection.Preferences);

				await connection.OutputFunction(transformedData);
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
		logger.LogInformation("[KAFKA-RECV] Disconnecting connection {Handle}. Reason: {Reason}",
			message.Handle, message.Reason ?? "None");

		await connectionService.DisconnectAsync(message.Handle);
	}
}