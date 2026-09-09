using MarkupString;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Globalization;

namespace SharpMUSH.Server.Consumers;

/// <summary>
/// Consumes telnet input messages from NATS JetStream and processes them
/// </summary>
public class TelnetInputConsumer(ILogger<TelnetInputConsumer> logger, ITaskScheduler scheduler, IConnectionService? connectionService = null)
	: IMessageConsumer<TelnetInputMessage>
{
	public async Task HandleAsync(TelnetInputMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("[NATS-RECV] TelnetInputMessage received - Handle: {Handle}, InputLength: {InputLength}",
			message.Handle, message.Input?.Length ?? 0);

		try
		{
			if (!await ConnectionIncarnation.WaitForRegistrationAsync(connectionService, message.Handle, message.SessionId, cancellationToken)) return;

			if (string.IsNullOrWhiteSpace(message.Input))
			{
				logger.LogDebug("[NATS-RECV] TelnetInputMessage ignored - empty input for Handle: {Handle}", message.Handle);
				return;
			}

			await scheduler.WriteUserCommand(
				handle: message.Handle,
				command: MarkupText.Plain(message.Input),
				state: ParserState.Empty with { Handle = message.Handle, ConnectionSessionId = message.SessionId });
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, "Error processing telnet input from handle {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes WebSocket input messages from NATS JetStream and processes them.
/// WebSocket connections publish <see cref="WebSocketInputMessage"/> rather than
/// <see cref="TelnetInputMessage"/>; both are processed identically by the MUSH engine.
/// </summary>
public class WebSocketInputConsumer(ILogger<WebSocketInputConsumer> logger, ITaskScheduler scheduler, IConnectionService? connectionService = null)
	: IMessageConsumer<WebSocketInputMessage>
{
	public async Task HandleAsync(WebSocketInputMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("[NATS-RECV] WebSocketInputMessage received - Handle: {Handle}, InputLength: {InputLength}",
			message.Handle, message.Input?.Length ?? 0);

		try
		{
			if (!await ConnectionIncarnation.WaitForRegistrationAsync(connectionService, message.Handle, message.SessionId, cancellationToken)) return;

			if (string.IsNullOrWhiteSpace(message.Input))
			{
				logger.LogDebug("[NATS-RECV] WebSocketInputMessage ignored - empty input for Handle: {Handle}", message.Handle);
				return;
			}

			await scheduler.WriteUserCommand(
				handle: message.Handle,
				command: MarkupText.Plain(message.Input),
				state: ParserState.Empty with { Handle = message.Handle, ConnectionSessionId = message.SessionId });
		}
		catch (Exception ex)
		{
			logger.LogCritical(ex, "Error processing WebSocket input from handle {Handle}", message.Handle);
		}
	}
}

/// <summary>
/// Consumes GMCP signal messages from NATS JetStream
/// </summary>
public class GMCPSignalConsumer(ILogger<GMCPSignalConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<GMCPSignalMessage>
{
	public Task HandleAsync(GMCPSignalMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("[NATS-RECV] GMCPSignalMessage received - Handle: {Handle}, Package: {Package}, Info: {Info}",
			message.Handle, message.Package, message.Info);

		connectionService.Update(message.Handle, "GMCP", "1");

		connectionService.Update(message.Handle, $"GMCP_{message.Package}", message.Info);

		HandleGMCPPackage(message.Handle, message.Package, message.Info);

		return Task.CompletedTask;
	}

	private void HandleGMCPPackage(long handle, string package, string info)
	{
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
/// Consumes MSDP update messages from NATS JetStream
/// </summary>
public class MSDPUpdateConsumer(ILogger<MSDPUpdateConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<MSDPUpdateMessage>
{
	public Task HandleAsync(MSDPUpdateMessage message, CancellationToken cancellationToken = default)
	{
		if (logger.IsEnabled(LogLevel.Debug))
		{
			logger.LogDebug("[NATS-RECV] MSDPUpdateMessage received - Handle: {Handle}, Variables: {Variables}",
				message.Handle, string.Join(", ", message.Variables.Select(kv => $"{kv.Key}={kv.Value}")));
		}

		foreach (var variable in message.Variables)
		{
			connectionService.Update(message.Handle, $"MSDP_{variable.Key}", variable.Value);
		}

		HandleMSDPVariables(message.Handle, message.Variables);

		return Task.CompletedTask;
	}

	private void HandleMSDPVariables(long handle, Dictionary<string, string> variables)
	{
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
/// Consumes NAWS update messages from NATS JetStream
/// </summary>
public class NAWSUpdateConsumer(ILogger<NAWSUpdateConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<NAWSUpdateMessage>
{
	public Task HandleAsync(NAWSUpdateMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("[NATS-RECV] NAWSUpdateMessage received - Handle: {Handle}, Width: {Width}, Height: {Height}",
			message.Handle, message.Width, message.Height);

		connectionService.Update(message.Handle, "HEIGHT", message.Height.ToString(CultureInfo.InvariantCulture));
		connectionService.Update(message.Handle, "WIDTH", message.Width.ToString(CultureInfo.InvariantCulture));

		return Task.CompletedTask;
	}
}

/// <summary>
/// Consumes connection established messages from NATS JetStream
/// </summary>
public class ConnectionEstablishedConsumer(
	ILogger<ConnectionEstablishedConsumer> logger,
	IConnectionService connectionService,
	IMessageBus bus,
	IConnectionStateStore? stateStore = null)
	: IMessageConsumer<ConnectionEstablishedMessage>
{
	public async Task HandleAsync(ConnectionEstablishedMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("[NATS-RECV] ConnectionEstablishedMessage received - Handle: {Handle}, IP: {IpAddress}, Hostname: {Hostname}, Type: {ConnectionType}, Timestamp: {Timestamp}",
			message.Handle, message.IpAddress, message.Hostname, message.ConnectionType, message.Timestamp);

		logger.LogInformation("Connection established: Handle {Handle}, IP {IpAddress}, Type {ConnectionType}",
			message.Handle, message.IpAddress, message.ConnectionType);

		if (!string.IsNullOrEmpty(message.SessionId) && stateStore is not null)
		{
			var persisted = await stateStore.GetConnectionAsync(message.Handle, cancellationToken);
			if (persisted?.Metadata.GetValueOrDefault("SessionId") != message.SessionId) return;
		}

		await connectionService.Register(message.Handle,
			message.IpAddress,
			message.Hostname,
			message.ConnectionType,
			async x => await bus.Publish(new TelnetOutputMessage(message.Handle, x)),
			async x => await bus.Publish(new TelnetPromptMessage(message.Handle, x)),
			() => System.Text.Encoding.UTF8,
			new System.Collections.Concurrent.ConcurrentDictionary<string, string>(new Dictionary<string, string>
			{
				{ "ConnectionStartTime", message.Timestamp.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) },
				{ "ConnectionIncarnationTime", (message.Timestamp.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks).ToString(CultureInfo.InvariantCulture) },
				{ "LastConnectionSignal", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() },
				{ "InternetProtocolAddress", message.IpAddress },
				{ "HostName", message.Hostname },
				{ "ConnectionType", message.ConnectionType },
				{ "PresenceClass", message.PresenceClass },
				{ "SessionId", message.SessionId ?? "" },
				// PennMUSH's CONN_SSL: what ssl() answers and terminfo()'s "ssl" token. Established at
				// accept time from the transport, so it is known before the connection can ask.
				{ "SSL", message.IsSecure ? "1" : "0" }
			}));
	}
}

/// <summary>
/// Consumes Pueblo negotiated messages — sets OUTPUT_FORMAT metadata on the connection.
/// Only upgrades to Pueblo if not already MXP (MXP is a superset of Pueblo).
/// </summary>
public class PuebloNegotiatedConsumer(ILogger<PuebloNegotiatedConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<PuebloNegotiatedMessage>
{
	internal static async Task<bool> WaitForConnectionRegistration(
		IConnectionService connectionService,
		long handle,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < ConnectionRetryPolicy.MaxAttempts; attempt++)
		{
			if (connectionService.Get(handle) is not null)
			{
				return true;
			}

			try
			{
				await Task.Delay(ConnectionRetryPolicy.Delay, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		return connectionService.Get(handle) is not null;
	}

	public async Task HandleAsync(PuebloNegotiatedMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[NATS-RECV] PuebloNegotiatedMessage - Handle: {Handle}, Client: {Client}",
			message.Handle, message.ClientResponse);

		if (!await WaitForConnectionRegistration(connectionService, message.Handle, cancellationToken))
		{
			logger.LogDebug("Dropping Pueblo negotiation for unregistered handle {Handle}", message.Handle);
			return;
		}

		var conn = connectionService.Get(message.Handle);
		if (conn?.Metadata.GetValueOrDefault("OUTPUT_FORMAT", "ansi") != "mxp")
		{
			connectionService.Update(message.Handle, "OUTPUT_FORMAT", "pueblo");
		}

		// Keep PUEBLO=1 for backward compat (pueblo() function reads it)
		connectionService.Update(message.Handle, "PUEBLO", "1");

	}
}

/// <summary>
/// Consumes MXP negotiated messages — sets OUTPUT_FORMAT to "mxp" on the connection.
/// MXP is a superset of Pueblo, so it takes priority over any prior Pueblo negotiation.
/// </summary>
public class MxpNegotiatedConsumer(ILogger<MxpNegotiatedConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<MxpNegotiatedMessage>
{
	public async Task HandleAsync(MxpNegotiatedMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[NATS-RECV] MxpNegotiatedMessage - Handle: {Handle}", message.Handle);

		if (!await PuebloNegotiatedConsumer.WaitForConnectionRegistration(connectionService, message.Handle, cancellationToken))
		{
			logger.LogDebug("Dropping MXP negotiation for unregistered handle {Handle}", message.Handle);
			return;
		}

		connectionService.Update(message.Handle, "OUTPUT_FORMAT", "mxp");
		// MXP clients also understand Pueblo tags, so set PUEBLO=1 for pueblo() compat
		connectionService.Update(message.Handle, "PUEBLO", "1");
	}
}

/// <summary>
/// Consumes the "this client speaks telnet" signal — PennMUSH's CONN_TELNET, reported by
/// <c>terminfo()</c> as the "telnet" token.
/// </summary>
public class TelnetNegotiatedConsumer(ILogger<TelnetNegotiatedConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<TelnetNegotiatedMessage>
{
	public async Task HandleAsync(TelnetNegotiatedMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogTrace("[NATS-RECV] TelnetNegotiatedMessage - Handle: {Handle}", message.Handle);

		if (!await PuebloNegotiatedConsumer.WaitForConnectionRegistration(connectionService, message.Handle, cancellationToken))
		{
			logger.LogDebug("Dropping telnet negotiation for unregistered handle {Handle}", message.Handle);
			return;
		}

		connectionService.Update(message.Handle, "TELNET", "1");
	}
}

/// <summary>
/// Consumes RFC 1091 terminal type negotiation results — the client name <c>terminfo()</c> reports.
/// </summary>
public class TerminalTypeNegotiatedConsumer(
	ILogger<TerminalTypeNegotiatedConsumer> logger,
	IConnectionService connectionService)
	: IMessageConsumer<TerminalTypeNegotiatedMessage>
{
	public async Task HandleAsync(TerminalTypeNegotiatedMessage message, CancellationToken cancellationToken = default)
	{
		if (logger.IsEnabled(LogLevel.Trace))
		{
			logger.LogTrace("[NATS-RECV] TerminalTypeNegotiatedMessage - Handle: {Handle}, Types: {TerminalTypes}",
				message.Handle, string.Join(", ", message.TerminalTypes));
		}

		if (message.TerminalTypes.Count == 0)
		{
			return;
		}

		if (!await PuebloNegotiatedConsumer.WaitForConnectionRegistration(connectionService, message.Handle, cancellationToken))
		{
			logger.LogDebug("Dropping terminal type negotiation for unregistered handle {Handle}", message.Handle);
			return;
		}

		// MTTS orders the responses client name, terminal type, then "MTTS <bitvector>", and RFC 1091
		// clients that know nothing of MTTS send a single terminal name. Either way the first entry is
		// what PennMUSH's terminfo() calls the client, so that is the one @sockset and terminfo() read.
		connectionService.Update(message.Handle, "TerminalType", message.TerminalTypes[0]);

		// The whole list, for the capability claims rather than the name: TelnetNegotiationCore has
		// already expanded an MTTS bitvector into names by this point, so this is where "256 COLORS",
		// "TRUECOLOR" and "SCREEN_READER" arrive. Tab-separated because those names contain spaces.
		connectionService.Update(message.Handle, "TerminalTypes", string.Join("\t", message.TerminalTypes));
	}
}

/// <summary>
/// Consumes connection closed messages from NATS JetStream
/// </summary>
public class ConnectionClosedConsumer(ILogger<ConnectionClosedConsumer> logger, IConnectionService connectionService)
	: IMessageConsumer<ConnectionClosedMessage>
{
	public async Task HandleAsync(ConnectionClosedMessage message, CancellationToken cancellationToken = default)
	{
		logger.LogDebug("[NATS-RECV] ConnectionClosedMessage received - Handle: {Handle}, Timestamp: {Timestamp}",
			message.Handle, message.Timestamp);

		logger.LogInformation("Connection closed: Handle {Handle}", message.Handle);

		if (!string.IsNullOrEmpty(message.SessionId) &&
			connectionService.Get(message.Handle)?.Metadata.GetValueOrDefault("SessionId") != message.SessionId) return;
		await connectionService.Disconnect(message.Handle, message.SessionId);
	}
}

internal static class ConnectionIncarnation
{
	public static async Task<bool> WaitForRegistrationAsync(IConnectionService? connections,
		long handle, string? session, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(session)) return true;
		// Input and registration use separate subjects: a fast first command may arrive first.
		for (var attempt = 0; attempt < ConnectionRetryPolicy.MaxAttempts; attempt++)
		{
			if (connections?.Get(handle)?.Metadata.GetValueOrDefault("SessionId") == session) return true;
			await Task.Delay(ConnectionRetryPolicy.Delay, ct);
		}
		return connections?.Get(handle)?.Metadata.GetValueOrDefault("SessionId") == session;
	}
}
