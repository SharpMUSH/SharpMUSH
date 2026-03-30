using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Server;

public class StartupHandler(
	ILogger<StartupHandler> logger,
	IExpandedObjectDataService data,
	IOptionsWrapper<SharpMUSHOptions> options,
	IMessageBus messageBus)
	: IHostedService
{
	private const string ServerVersion = "1.0.0";

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Setting server time data.");
		// Initialize uptime data with current time. NextWarningTime and NextPurgeTime
		// will be managed by ScheduledTaskManagementService based on configuration.
		await data.SetExpandedServerDataAsync(new UptimeData(
			StartTime: DateTimeOffset.UtcNow,
			LastRebootTime: DateTimeOffset.Now,
			Reboots: 0,
			NextWarningTime: DateTimeOffset.UtcNow + TimeSpan.FromDays(1),
			NextPurgeTime: DateTimeOffset.UtcNow + TimeSpan.FromDays(1)
		));

		var existingMotd = await data.GetExpandedServerDataAsync<MotdData>();
		if (existingMotd is null)
		{
			logger.LogInformation("Seeding default MOTD data.");
			await data.SetExpandedServerDataAsync(new MotdData());
		}
		else
		{
			logger.LogDebug("Default MOTD data already present; skipping seeding.");
		}

		logger.LogInformation("Initializing configurable aliases and restrictions from database.");
		var currentOptions = options.CurrentValue;
		Configurable.Initialize(currentOptions.Alias, currentOptions.Restriction);

		// Notify ConnectionServer that the main process is ready
		logger.LogInformation("Publishing MainProcessReadyMessage to ConnectionServer.");
		await messageBus.Publish(new MainProcessReadyMessage(DateTimeOffset.UtcNow, ServerVersion), cancellationToken);
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		// Notify ConnectionServer that the main process is shutting down
		logger.LogInformation("Publishing MainProcessShutdownMessage to ConnectionServer.");
		try
		{
			await messageBus.Publish(new MainProcessShutdownMessage(DateTimeOffset.UtcNow, "Server shutting down"), cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			logger.LogDebug("Shutdown message publishing cancelled.");
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to publish MainProcessShutdownMessage during shutdown");
		}
	}
}