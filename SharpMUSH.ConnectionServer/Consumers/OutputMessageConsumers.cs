using SharpMUSH.Messaging.Adapters;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes telnet output messages from MainProcess and sends to connections
/// </summary>
public class TelnetOutputConsumer(IConnectionServerService connectionService, ILogger<TelnetOutputConsumer> logger)
	: IConsumer<TelnetOutputMessage>
{
	public async Task Consume(ConsumeContext<TelnetOutputMessage> context)
	{
		var message = context.Message;
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
/// Consumes telnet prompt messages from MainProcess and sends to connections
/// </summary>
public class TelnetPromptConsumer(IConnectionServerService connectionService, ILogger<TelnetPromptConsumer> logger)
	: IConsumer<TelnetPromptMessage>
{
	public async Task Consume(ConsumeContext<TelnetPromptMessage> context)
	{
		var message = context.Message;
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
/// Consumes broadcast messages from MainProcess and sends to all connections
/// </summary>
public class BroadcastConsumer(IConnectionServerService connectionService, ILogger<BroadcastConsumer> logger)
	: IConsumer<BroadcastMessage>
{
	public async Task Consume(ConsumeContext<BroadcastMessage> context)
	{
		var message = context.Message;
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
/// Consumes disconnect commands from MainProcess
/// </summary>
public class DisconnectConnectionConsumer(
	IConnectionServerService connectionService,
	ILogger<DisconnectConnectionConsumer> logger)
	: IConsumer<DisconnectConnectionMessage>
{
	public async Task Consume(ConsumeContext<DisconnectConnectionMessage> context)
	{
		var message = context.Message;
		logger.LogInformation("Disconnecting connection {Handle}. Reason: {Reason}", 
			message.Handle, message.Reason ?? "None");

		await connectionService.DisconnectAsync(message.Handle);
	}
}
