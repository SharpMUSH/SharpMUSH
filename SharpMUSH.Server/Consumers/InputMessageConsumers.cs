using MassTransit;
using MarkupString;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;

namespace SharpMUSH.Server.Consumers;

/// <summary>
/// Consumes telnet input messages from ConnectionServer and processes them
/// </summary>
public class TelnetInputConsumer : IConsumer<TelnetInputMessage>
{
	private readonly ILogger<TelnetInputConsumer> _logger;
	private readonly ITaskScheduler _scheduler;

	public TelnetInputConsumer(ILogger<TelnetInputConsumer> logger, ITaskScheduler scheduler)
	{
		_logger = logger;
		_scheduler = scheduler;
	}

	public async Task Consume(ConsumeContext<TelnetInputMessage> context)
	{
		var message = context.Message;

		try
		{
			await _scheduler.WriteUserCommand(
				handle: message.Handle,
				command: MarkupStringModule.single(message.Input),
				state: ParserState.Empty with { Handle = message.Handle });
		}
		catch (Exception ex)
		{
			_logger.LogCritical(ex, "Error processing telnet input from handle {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes GMCP signal messages from ConnectionServer
/// </summary>
public class GMCPSignalConsumer : IConsumer<GMCPSignalMessage>
{
	private readonly ILogger<GMCPSignalConsumer> _logger;

	public GMCPSignalConsumer(ILogger<GMCPSignalConsumer> logger)
	{
		_logger = logger;
	}

	public Task Consume(ConsumeContext<GMCPSignalMessage> context)
	{
		var message = context.Message;
		_logger.LogDebug("Received GMCP signal from handle {Handle}: {Package} - {Info}", 
			message.Handle, message.Package, message.Info);

		// TODO: Implement GMCP signal handling
		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes MSDP update messages from ConnectionServer
/// </summary>
public class MSDPUpdateConsumer : IConsumer<MSDPUpdateMessage>
{
	private readonly ILogger<MSDPUpdateConsumer> _logger;

	public MSDPUpdateConsumer(ILogger<MSDPUpdateConsumer> logger)
	{
		_logger = logger;
	}

	public Task Consume(ConsumeContext<MSDPUpdateMessage> context)
	{
		var message = context.Message;
		_logger.LogDebug("Received MSDP update from handle {Handle} with {Count} variables", 
			message.Handle, message.Variables.Count);

		// TODO: Implement MSDP update handling
		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes NAWS update messages from ConnectionServer
/// </summary>
public class NAWSUpdateConsumer : IConsumer<NAWSUpdateMessage>
{
	private readonly ILogger<NAWSUpdateConsumer> _logger;

	public NAWSUpdateConsumer(ILogger<NAWSUpdateConsumer> logger)
	{
		_logger = logger;
	}

	public Task Consume(ConsumeContext<NAWSUpdateMessage> context)
	{
		var message = context.Message;
		_logger.LogDebug("Received NAWS update from handle {Handle}: {Width}x{Height}", 
			message.Handle, message.Width, message.Height);

		// TODO: Implement NAWS update handling (update connection metadata)
		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes connection established messages from ConnectionServer
/// </summary>
public class ConnectionEstablishedConsumer : IConsumer<ConnectionEstablishedMessage>
{
	private readonly ILogger<ConnectionEstablishedConsumer> _logger;

	public ConnectionEstablishedConsumer(ILogger<ConnectionEstablishedConsumer> logger)
	{
		_logger = logger;
	}

	public Task Consume(ConsumeContext<ConnectionEstablishedMessage> context)
	{
		var message = context.Message;
		_logger.LogInformation("Connection established: Handle {Handle}, IP {IpAddress}, Type {ConnectionType}", 
			message.Handle, message.IpAddress, message.ConnectionType);

		// TODO: Initialize any connection-specific state in the database or services
		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes connection closed messages from ConnectionServer
/// </summary>
public class ConnectionClosedConsumer : IConsumer<ConnectionClosedMessage>
{
	private readonly ILogger<ConnectionClosedConsumer> _logger;

	public ConnectionClosedConsumer(ILogger<ConnectionClosedConsumer> logger)
	{
		_logger = logger;
	}

	public Task Consume(ConsumeContext<ConnectionClosedMessage> context)
	{
		var message = context.Message;
		_logger.LogInformation("Connection closed: Handle {Handle}", message.Handle);

		// TODO: Clean up any connection-specific state
		return Task.CompletedTask;
	}
}
