using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes <see cref="TelnetOutputMessage"/> from NATS JetStream and writes transformed
/// bytes directly to the corresponding TCP connection.
/// </summary>
public class TelnetOutputConsumer(
	IConnectionServerService connectionService,
	IOutputTransformService transformService,
	ILogger<TelnetOutputConsumer> logger)
	: IMessageConsumer<TelnetOutputMessage>
{
	public async Task HandleAsync(TelnetOutputMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[NATS-RECV] TelnetOutputMessage received — Handle: {Handle}, DataLength: {DataLength}",
			message.Handle, message.Data?.Length ?? 0);

		var connection = connectionService.Get(message.Handle);

		if (connection == null)
		{
			logger.LogWarning("Received output for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		if (message.Data is null || message.Data.Length == 0)
			return;

		try
		{
			var transformedData = await transformService.TransformAsync(
				message.Data,
				connection.Capabilities,
				connection.Preferences);

			await connection.OutputFunction(transformedData);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error sending output to connection {Handle}", message.Handle);
		}
	}
}
