using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server;

public class StartupHandler(
	ILogger<StartupHandler> logger, 
	IExpandedObjectDataService data,
	IOptionsWrapper<SharpMUSHOptions> options)
	: IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Setting server time data.");
		// Future Enhancement: Move CRON/scheduled task management (warning times, purge times, etc.)
		// to a dedicated background service. This would better separate concerns and allow for
		// more sophisticated scheduling logic (e.g., configurable schedules, multiple cron jobs).
		await data.SetExpandedServerDataAsync(new UptimeData(
			StartTime: DateTimeOffset.UtcNow,
			LastRebootTime: DateTimeOffset.Now,
			Reboots: 0,
			NextWarningTime: DateTimeOffset.UtcNow + TimeSpan.FromDays(1),
			NextPurgeTime: DateTimeOffset.UtcNow + TimeSpan.FromDays(1)
		));

		logger.LogInformation("Initializing configurable aliases and restrictions from database.");
		var currentOptions = options.CurrentValue;
		Configurable.Initialize(currentOptions.Alias, currentOptions.Restriction);
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}