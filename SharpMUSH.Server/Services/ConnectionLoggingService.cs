using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Background service that listens to connection state changes and logs them using structured logging.
/// </summary>
public class ConnectionLoggingService(IConnectionService connectionService, ILogger<ConnectionLoggingService> logger)
	: IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		connectionService.ListenState(LogConnectionStateChange);
		logger.LogInformation("Connection logging service started");
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Connection logging service stopped");
		return Task.CompletedTask;
	}

	private void LogConnectionStateChange((long Handle, Library.Models.DBRef? Ref, IConnectionService.ConnectionState OldState, IConnectionService.ConnectionState NewState) change)
	{
		var (handle, dbRef, oldState, newState) = change;

		using (logger.BeginScope(new Dictionary<string, string>
		{
			["Category"] = "Connection",
			["Handle"] = handle.ToString(),
			["DBRef"] = dbRef?.ToString() ?? "null",
			["OldState"] = oldState.ToString(),
			["NewState"] = newState.ToString()
		}))
		{
			logger.LogInformation(
				"Connection state changed: Handle={Handle} DBRef={DBRef} {OldState} -> {NewState}",
				handle, dbRef?.ToString() ?? "null", oldState, newState);
		}
	}
}
