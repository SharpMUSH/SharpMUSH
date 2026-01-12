using SharpMUSH.Messaging.Adapters;
using MarkupString;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using System.Globalization;

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
			if (string.IsNullOrWhiteSpace(message.Input))
			{
				// Protect the parser from interpreting empty input.
				return;
			}
			
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
public class GMCPSignalConsumer(ILogger<GMCPSignalConsumer> logger, IConnectionService connectionService) : IConsumer<GMCPSignalMessage>
{
	public Task Consume(ConsumeContext<GMCPSignalMessage> context)
	{
		var message = context.Message;
		logger.LogDebug("Received GMCP signal from handle {Handle}: {Package} - {Info}",
			message.Handle, message.Package, message.Info);

		// Store GMCP package and info in connection metadata
		connectionService.Update(message.Handle, $"GMCP_{message.Package}", message.Info);
		
		// Handle specific GMCP packages
		HandleGMCPPackage(message.Handle, message.Package, message.Info);
		
		return Task.CompletedTask;
	}
	
	private void HandleGMCPPackage(long handle, string package, string info)
	{
		// Process specific GMCP packages
		switch (package)
		{
			case "Core.Hello":
				logger.LogInformation("Client {Handle} sent Core.Hello: {Info}", handle, info);
				connectionService.Update(handle, "GMCP_ClientHello", info);
				break;
				
			case "Core.Supports.Set":
				logger.LogInformation("Client {Handle} supports: {Info}", handle, info);
				connectionService.Update(handle, "GMCP_ClientSupports", info);
				break;
				
			case "Char.Vitals":
				logger.LogDebug("Client {Handle} sent Char.Vitals update", handle);
				break;
				
			case "Comm.Channel":
				logger.LogDebug("Client {Handle} sent Comm.Channel data", handle);
				break;
				
			default:
				logger.LogDebug("Unhandled GMCP package {Package} from handle {Handle}", package, handle);
				break;
		}
	}
}

/// <summary>
/// Consumes MSDP update messages from ConnectionServer
/// </summary>
public class MSDPUpdateConsumer(ILogger<MSDPUpdateConsumer> logger, IConnectionService connectionService) : IConsumer<MSDPUpdateMessage>
{
	public Task Consume(ConsumeContext<MSDPUpdateMessage> context)
	{
		var message = context.Message;
		logger.LogDebug("Received MSDP update from handle {Handle} with {Count} variables",
			message.Handle, message.Variables.Count);

		// Store each MSDP variable in connection metadata
		foreach (var variable in message.Variables)
		{
			connectionService.Update(message.Handle, $"MSDP_{variable.Key}", variable.Value);
		}
		
		// Handle specific MSDP variables
		HandleMSDPVariables(message.Handle, message.Variables);
		
		return Task.CompletedTask;
	}
	
	private void HandleMSDPVariables(long handle, Dictionary<string, string> variables)
	{
		// Process specific MSDP variables
		foreach (var variable in variables)
		{
			switch (variable.Key)
			{
				case "CLIENT_NAME":
					logger.LogInformation("Client {Handle} name: {ClientName}", handle, variable.Value);
					connectionService.Update(handle, "ClientName", variable.Value);
					break;
					
				case "CLIENT_VERSION":
					logger.LogInformation("Client {Handle} version: {ClientVersion}", handle, variable.Value);
					connectionService.Update(handle, "ClientVersion", variable.Value);
					break;
					
				case "REPORTABLE_VARIABLES":
					logger.LogDebug("Client {Handle} reportable variables: {Variables}", handle, variable.Value);
					break;
					
				case "TERMINAL_TYPE":
					logger.LogInformation("Client {Handle} terminal type: {TerminalType}", handle, variable.Value);
					connectionService.Update(handle, "TerminalType", variable.Value);
					break;
					
				default:
					logger.LogDebug("MSDP variable {Key}={Value} from handle {Handle}", variable.Key, variable.Value, handle);
					break;
			}
		}
	}
}

/// <summary>
/// Consumes NAWS update messages from ConnectionServer
/// </summary>
public class NAWSUpdateConsumer(ILogger<NAWSUpdateConsumer> logger, IConnectionService connectionService) : IConsumer<NAWSUpdateMessage>
{
	public Task Consume(ConsumeContext<NAWSUpdateMessage> context)
	{
		var message = context.Message;
		logger.LogDebug("Received NAWS update from handle {Handle}: {Width}x{Height}",
			message.Handle, message.Width, message.Height);

		// Update connection metadata with new window size
		connectionService.Update(message.Handle, "HEIGHT", message.Height.ToString(CultureInfo.InvariantCulture));
		connectionService.Update(message.Handle, "WIDTH", message.Width.ToString(CultureInfo.InvariantCulture));
		
		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes connection established messages from ConnectionServer
/// </summary>
public class ConnectionEstablishedConsumer(
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