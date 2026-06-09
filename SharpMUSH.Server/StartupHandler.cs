using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;

namespace SharpMUSH.Server;

public class StartupHandler(
	ILogger<StartupHandler> logger,
	IExpandedObjectDataService data,
	IOptionsWrapper<SharpMUSHOptions> options,
	IWikiService wikiService,
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

		// Seed the "home" wiki page. CreateAsync is a no-op if the slug already exists, so
		// this is safe on every restart.
		var homeResult = await wikiService.CreateAsync(
			title: "Home",
			markdown: """
				# Welcome to SharpMUSH!

				This is your MUSH's home page. It's stored as a wiki article and can be edited
				by any authorised user.

				## Getting started

				- Connect with a MU* client on port **4201**
				- Or use the terminal panel below
				- Create a character with `create <name> <password>`
				- Then log in with `connect <name> <password>`

				## About SharpMUSH

				SharpMUSH is a modern, open-source MUSH server written in .NET, targeting
				PennMUSH compatibility. See the [wiki](/wiki/wiki-index) for more information.
				""",
			authorDbref: "#1",
			ns: WikiNamespace.Main);
		homeResult.Switch(
			page => logger.LogInformation("Home wiki page seeded (id={Id}).", page.Id),
			err => logger.LogDebug("Home wiki page already exists; skipping seed. ({Msg})", err.Value));

		logger.LogInformation("Initializing configurable aliases and restrictions from database.");
		var currentOptions = options.CurrentValue;
		Configurable.Initialize(currentOptions.Alias, currentOptions.Restriction);
		Configurable.FloatPrecision = (int)currentOptions.Cosmetic.FloatPrecision;

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