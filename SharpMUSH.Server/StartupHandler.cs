using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server;

public class StartupHandler(ISharpDatabase database, IExpandedObjectDataService data, ILogger<StartupHandler> logger)
	: IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		logger.LogInformation("Starting Database");
		await database.Migrate();
		logger.LogInformation("Database is ready to go.");

		// TODO: Move handling of CRON information like this to its own background Handler
		await data.SetExpandedServerDataAsync(new UptimeData(
			StartTime: DateTimeOffset.UtcNow,
			LastRebootTime: DateTimeOffset.Now,
			Reboots: 0,
			NextWarningTime: DateTimeOffset.UtcNow + TimeSpan.FromDays(1),
			NextPurgeTime: DateTimeOffset.UtcNow + TimeSpan.FromDays(1)
		));
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
		throw new NotImplementedException();
	}
}