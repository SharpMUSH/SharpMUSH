using MarkupString;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Globalization;

namespace SharpMUSH.Server.Consumers;

/// <summary>
/// Consumes telnet input messages from Kafka and processes them
/// </summary>
public class TelnetInputConsumer(ILogger<TelnetInputConsumer> logger, ITaskScheduler scheduler)
	: IMessageConsumer<TelnetInputMessage>
{
	public async Task HandleAsync(TelnetInputMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[KAFKA-RECV] TelnetInputMessage received - Handle: {Handle}, Input: {Input}",
			message.Handle, message.Input);

		try
		{
			if (string.IsNullOrWhiteSpace(message.Input))
			{
				logger.LogTrace("[KAFKA-RECV] TelnetInputMessage ignored - empty input for Handle: {Handle}", message.Handle);
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
/// Consumes GMCP signal messages from Kafka
/// </summary>
public class GMCPSignalConsumer(ILogger<GMCPSignalConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<GMCPSignalMessage>
{
	public Task HandleAsync(GMCPSignalMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[KAFKA-RECV] GMCPSignalMessage received - Handle: {Handle}, Package: {Package}, Info: {Info}",
			message.Handle, message.Package, message.Info);

		logger.LogDebug("Received GMCP signal from handle {Handle}: {Package} - {Info}",
			message.Handle, message.Package, message.Info);

		// Set GMCP capability flag on first GMCP message
		connectionService.Update(message.Handle, "GMCP", "1");

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
/// Consumes MSDP update messages from Kafka
/// </summary>
public class MSDPUpdateConsumer(ILogger<MSDPUpdateConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<MSDPUpdateMessage>
{
	public Task HandleAsync(MSDPUpdateMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[KAFKA-RECV] MSDPUpdateMessage received - Handle: {Handle}, Variables: {Variables}",
			message.Handle, string.Join(", ", message.Variables.Select(kv => $"{kv.Key}={kv.Value}")));

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
/// Consumes NAWS update messages from Kafka
/// </summary>
public class NAWSUpdateConsumer(ILogger<NAWSUpdateConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<NAWSUpdateMessage>
{
	public Task HandleAsync(NAWSUpdateMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[KAFKA-RECV] NAWSUpdateMessage received - Handle: {Handle}, Width: {Width}, Height: {Height}",
			message.Handle, message.Width, message.Height);

		logger.LogDebug("Received NAWS update from handle {Handle}: {Width}x{Height}",
			message.Handle, message.Width, message.Height);

		// Update connection metadata with new window size
		connectionService.Update(message.Handle, "HEIGHT", message.Height.ToString(CultureInfo.InvariantCulture));
		connectionService.Update(message.Handle, "WIDTH", message.Width.ToString(CultureInfo.InvariantCulture));

		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes connection established messages from Kafka
/// </summary>
public class ConnectionEstablishedConsumer(
	ILogger<ConnectionEstablishedConsumer> logger,
	IConnectionService connectionService,
	IMessageBus bus)
	: IMessageConsumer<ConnectionEstablishedMessage>
{
	public Task HandleAsync(ConnectionEstablishedMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[KAFKA-RECV] ConnectionEstablishedMessage received - Handle: {Handle}, IP: {IpAddress}, Hostname: {Hostname}, Type: {ConnectionType}, Timestamp: {Timestamp}",
			message.Handle, message.IpAddress, message.Hostname, message.ConnectionType, message.Timestamp);

		logger.LogInformation("Connection established: Handle {Handle}, IP {IpAddress}, Type {ConnectionType}",
			message.Handle, message.IpAddress, message.ConnectionType);

		connectionService.Register(message.Handle,
			message.IpAddress,
			message.Hostname,
			message.ConnectionType,
			async x => await bus.Publish(new TelnetOutputMessage(message.Handle, x)),
			async x => await bus.Publish(new TelnetPromptMessage(message.Handle, x)),
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
/// Consumes connection closed messages from Kafka
/// </summary>
public class ConnectionClosedConsumer(ILogger<ConnectionClosedConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<ConnectionClosedMessage>
{
	public Task HandleAsync(ConnectionClosedMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[KAFKA-RECV] ConnectionClosedMessage received - Handle: {Handle}, Timestamp: {Timestamp}",
			message.Handle, message.Timestamp);

		logger.LogInformation("Connection closed: Handle {Handle}", message.Handle);

		connectionService.Disconnect(message.Handle);

		return Task.CompletedTask;
	}
}