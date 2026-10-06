using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// Asks the main process for the MSSP report when this connection server starts, so a restarted one
/// does not wait for the report to change before it answers the telnet option with it.
/// </summary>
public sealed class MsspReportRequestService(IMessageBus bus, ILogger<MsspReportRequestService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			await bus.Publish(new MSSPReportRequestMessage(DateTimeOffset.UtcNow), cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			// The main process also sends the report when it starts and whenever it changes.
			logger.LogWarning(ex, "Could not ask the main process for the MSSP report");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
