using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.SocketServer.Consumers;

/// <summary>
/// Asks a connection's terminal what it can draw (<c>SOCKSET graphics=detect</c>). Only a telnet
/// connection has a terminal to ask; the answers arrive with the player's next line.
/// </summary>
public class ProbeTerminalConsumer(TerminalProbes probes, ILogger<ProbeTerminalConsumer> logger)
	: IMessageConsumer<ProbeTerminalMessage>
{
	public async Task HandleAsync(ProbeTerminalMessage message, CancellationToken cancellationToken = default)
	{
		try
		{
			if (!await probes.ProbeAsync(message.Handle))
			{
				logger.LogDebug("No terminal to ask on handle {Handle}", message.Handle);
			}
		}
		catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
		{
			logger.LogDebug(ex, "Could not ask the terminal on handle {Handle}", message.Handle);
		}
	}
}
