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
public class TelnetInputConsumer(ILogger<TelnetInputConsumer> logger, ITaskScheduler scheduler)
	: IConsumer<TelnetInputMessage>
{
	public async Task Consume(ConsumeContext<TelnetInputMessage> context)
	{
		var message = context.Message;

		try
		{
			await scheduler.WriteUserCommand(
				handle: message.Handle,
				command: MarkupStringModule.single(message.Input),
				state: ParserState.Empty with { Handle = message.Handle });
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, "Error processing telnet input from handle {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes GMCP signal messages from ConnectionServer
/// </summary>
public class GMCPSignalConsumer(ILogger<GMCPSignalConsumer> logger) : IConsumer<GMCPSignalMessage>
{
	public Task Consume(ConsumeContext<GMCPSignalMessage> context)
	{
		var message = context.Message;
		logger.LogDebug("Received GMCP signal from handle {Handle}: {Package} - {Info}",
			message.Handle, message.Package, message.Info);

		// TODO: Implement GMCP signal handling
		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes MSDP update messages from ConnectionServer
/// </summary>
public class MSDPUpdateConsumer(ILogger<MSDPUpdateConsumer> logger) : IConsumer<MSDPUpdateMessage>
{
	public Task Consume(ConsumeContext<MSDPUpdateMessage> context)
	{
		var message = context.Message;
		logger.LogDebug("Received MSDP update from handle {Handle} with {Count} variables",
			message.Handle, message.Variables.Count);

		// TODO: Implement MSDP update handling
		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes NAWS update messages from ConnectionServer
/// </summary>
public class NAWSUpdateConsumer(ILogger<NAWSUpdateConsumer> logger) : IConsumer<NAWSUpdateMessage>
{
	public Task Consume(ConsumeContext<NAWSUpdateMessage> context)
	{
		var message = context.Message;
		logger.LogDebug("Received NAWS update from handle {Handle}: {Width}x{Height}",
			message.Handle, message.Width, message.Height);

		// TODO: Implement NAWS update handling (update connection metadata)
		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes connection established messages from ConnectionServer
/// </summary>
public abstract class ConnectionEstablishedConsumer(
	ILogger<ConnectionEstablishedConsumer> logger,
	IConnectionService connectionService,
	IBus bus)
	: IConsumer<ConnectionEstablishedMessage>
{
	public Task Consume(ConsumeContext<ConnectionEstablishedMessage> context)
	{
		var message = context.Message;
		logger.LogInformation("Connection established: Handle {Handle}, IP {IpAddress}, Type {ConnectionType}",
			message.Handle, message.IpAddress, message.ConnectionType);

		connectionService.Register(message.Handle,
			message.IpAddress,
			message.Hostname,
			message.ConnectionType,
			async x => await bus.Send(new TelnetOutputMessage(message.Handle, x)),
			async x => await bus.Send(new TelnetPromptMessage(message.Handle, x)),
			() => System.Text.Encoding.UTF8,
			new System.Collections.Concurrent.ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				{ "ConnectionStartTime", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
				{ "LastConnectionSignal", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
				{ "InternetProtocolAddress", message.IpAddress },
				{ "HostName", message.Hostname },
				{ "ConnectionType", message.ConnectionType }
			}));

		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes connection closed messages from ConnectionServer
/// </summary>
public class ConnectionClosedConsumer(ILogger<ConnectionClosedConsumer> logger, IConnectionService connectionService) : IConsumer<ConnectionClosedMessage>
{
	public Task Consume(ConsumeContext<ConnectionClosedMessage> context)
	{
		var message = context.Message;
		logger.LogInformation("Connection closed: Handle {Handle}", message.Handle);

		connectionService.Disconnect(message.Handle);
		
		return Task.CompletedTask;
	}
}