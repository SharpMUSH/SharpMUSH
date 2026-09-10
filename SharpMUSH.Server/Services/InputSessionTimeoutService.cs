using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>Expires bounded capture records and submits callbacks through normal admission.</summary>
public sealed class InputSessionTimeoutService(IInputSessionService sessions, ITaskScheduler scheduler,
	INotifyService notify, IConnectionService connections, ILogger<InputSessionTimeoutService> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
		while (await timer.WaitForNextTickAsync(stoppingToken))
		{
			foreach (var session in sessions.TakeExpired())
			{
				try
				{
					var admission = await scheduler.WriteInputSessionTimeout(session);
					if (admission.Accepted) continue;
					sessions.Discard(session);
					if (ReferenceEquals(connections.Get(session.Connection.Handle), session.Connection)
						&& session.Connection.Metadata.GetValueOrDefault("SessionId") == session.TransportSessionId)
						await notify.NotifyLocalizedToSession(session.Connection.Handle, session.TransportSessionId ?? "", "InputSessionTimeoutRejected");
				}
				catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
				{
					sessions.Discard(session);
					logger.LogError(ex, "Input session timeout failed for handle {Handle}", session.Connection.Handle);
				}
			}
		}
	}
}
