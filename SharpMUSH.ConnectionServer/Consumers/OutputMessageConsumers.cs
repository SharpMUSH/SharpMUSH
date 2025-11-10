using MassTransit;
using Microsoft.Extensions.Logging;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messages;

namespace SharpMUSH.ConnectionServer.Consumers;

/// <summary>
/// Consumes telnet output messages from MainProcess and sends to connections
/// </summary>
public class TelnetOutputConsumer : IConsumer<TelnetOutputMessage>
{
	private readonly IConnectionService _connectionService;
	private readonly ILogger<TelnetOutputConsumer> _logger;

	public TelnetOutputConsumer(IConnectionService connectionService, ILogger<TelnetOutputConsumer> logger)
	{
		_connectionService = connectionService;
		_logger = logger;
	}

	public async Task Consume(ConsumeContext<TelnetOutputMessage> context)
	{
		var message = context.Message;
		var connection = _connectionService.Get(message.Handle);

		if (connection == null)
		{
			_logger.LogWarning("Received output for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			await connection.OutputFunction(message.Data);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error sending output to connection {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes telnet prompt messages from MainProcess and sends to connections
/// </summary>
public class TelnetPromptConsumer : IConsumer<TelnetPromptMessage>
{
	private readonly IConnectionService _connectionService;
	private readonly ILogger<TelnetPromptConsumer> _logger;

	public TelnetPromptConsumer(IConnectionService connectionService, ILogger<TelnetPromptConsumer> logger)
	{
		_connectionService = connectionService;
		_logger = logger;
	}

	public async Task Consume(ConsumeContext<TelnetPromptMessage> context)
	{
		var message = context.Message;
		var connection = _connectionService.Get(message.Handle);

		if (connection == null)
		{
			_logger.LogWarning("Received prompt for unknown connection handle: {Handle}", message.Handle);
			return;
		}

		try
		{
			await connection.PromptOutputFunction(message.Data);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error sending prompt to connection {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes broadcast messages from MainProcess and sends to all connections
/// </summary>
public class BroadcastConsumer : IConsumer<BroadcastMessage>
{
	private readonly IConnectionService _connectionService;
	private readonly ILogger<BroadcastConsumer> _logger;

	public BroadcastConsumer(IConnectionService connectionService, ILogger<BroadcastConsumer> logger)
	{
		_connectionService = connectionService;
		_logger = logger;
	}

	public async Task Consume(ConsumeContext<BroadcastMessage> context)
	{
		var message = context.Message;
		var connections = _connectionService.GetAll();

		foreach (var connection in connections)
		{
			try
			{
				await connection.OutputFunction(message.Data);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error broadcasting to connection {Handle}", connection.Handle);
			}
		}
	}
}

/// <summary>
/// Consumes disconnect commands from MainProcess
/// </summary>
public class DisconnectConnectionConsumer : IConsumer<DisconnectConnectionMessage>
{
	private readonly IConnectionService _connectionService;
	private readonly ILogger<DisconnectConnectionConsumer> _logger;

	public DisconnectConnectionConsumer(IConnectionService connectionService, ILogger<DisconnectConnectionConsumer> logger)
	{
		_connectionService = connectionService;
		_logger = logger;
	}

	public async Task Consume(ConsumeContext<DisconnectConnectionMessage> context)
	{
		var message = context.Message;
		_logger.LogInformation("Disconnecting connection {Handle}. Reason: {Reason}", 
			message.Handle, message.Reason ?? "None");

		await _connectionService.DisconnectAsync(message.Handle);
	}
}
