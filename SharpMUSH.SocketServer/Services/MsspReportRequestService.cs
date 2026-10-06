using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.SocketServer.Services;

/// <summary>
/// Asks the main process for the MSSP report when this connection server starts, so a restarted one
/// does not wait for the report to change before it answers the telnet option with it. It asks again,
/// further apart each time, until a report arrives: the first request can be answered before this
/// server's consumer exists (its first start, or a recreated stream), and an unchanged report is not
/// sent again on its own.
/// </summary>
public sealed class MsspReportRequestService(IMessageBus bus, MsspReportHolder holder, ILogger<MsspReportRequestService> logger)
	: BackgroundService
{
	private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan LongestRetry = TimeSpan.FromMinutes(1);

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var retry = FirstRetry;
		while (!holder.Received.IsCompleted && !stoppingToken.IsCancellationRequested)
		{
			try
			{
				await bus.Publish(new MSSPReportRequestMessage(DateTimeOffset.UtcNow), stoppingToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				logger.LogWarning(ex, "Could not ask the main process for the MSSP report");
			}

			await Task.WhenAny(holder.Received, Task.Delay(retry, stoppingToken));
			retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LongestRetry.Ticks));
		}
	}
}
