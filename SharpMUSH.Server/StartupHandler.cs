using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Server.Resources;

namespace SharpMUSH.Server;

public class StartupHandler(
	ILogger<StartupHandler> logger,
	IExpandedObjectDataService data,
	IOptionsWrapper<SharpMUSHOptions> options,
	IWikiService wikiService,
	IMessageBus messageBus,
	SharpMUSH.Messaging.NATS.NatsConsumerRegistry? consumers = null)
	: IHostedLifecycleService, IDisposable
{
	private const string ServerVersion = "1.0.0";
	private readonly CancellationTokenSource _readinessCancellation = new();
	private Task _ready = Task.CompletedTask;

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

		// Wiki seeding must never abort startup: a seeding failure (e.g. a stale DB whose data
		// predates the current schema) must not prevent MainProcessReadyMessage, which the
		// ConnectionServer waits on before accepting logins. Failures are logged and skipped.
		try
		{
			await SeedWikiPagesAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Wiki page seeding failed; continuing startup without it.");
		}

		logger.LogInformation("Initializing configurable aliases and restrictions from database.");
		var currentOptions = options.CurrentValue;
		Configurable.Initialize(currentOptions.Alias, currentOptions.Restriction);
		Configurable.FloatPrecision = (int)currentOptions.Cosmetic.FloatPrecision;

	}

	/// <summary>
	/// Seeds the default Home, Markdown Guide, and Application Schema Guide wiki pages (idempotent — no-op
	/// if present). The pages are English, so they are stamped with an explicit <c>SourceLocale</c>: that
	/// records a fact about their content rather than deferring to whatever a game configures as its
	/// default, which is why <c>"en"</c> is literal here.
	/// No translations are seeded: machine-quality translated help is worse than a gap the reader's
	/// fallback notice makes actionable, so translating these is content work tracked separately.
	/// </summary>
	private async Task SeedWikiPagesAsync()
	{
		// Seed the "home" wiki page. CreateAsync is a no-op if the slug already exists, so
		// this is safe on every restart.
		var homeResult = await wikiService.CreateAsync(
			title: "Home",
			markdown: """
				![SharpMUSH logo](/assets/Logo.svg){width=20%}
				
				This is your MUSH's home page. It's stored as a wiki article and can be edited
				by any authorised user.

				## Getting started
				- Connect with a MU* client on port **4201**
				- Or use the terminal panel below
				- Create a character with `create <name> <password>`
				- Then log in with `connect <name> <password>`

				## About SharpMUSH
				SharpMUSH is a modern, open-source MUSH server written in .NET, targeting
				PennMUSH compatibility. See the [[Help:Markdown Guide]] for formatting help.
				""",
			authorDbref: "#1",
			ns: WikiNamespace.Main,
			category: "general",
			sourceLocale: "en");
		switch (homeResult)
		{
			case WikiPage page:
				logger.LogInformation("Home wiki page seeded (id={Id}).", page.Id);
				break;
			case Error<string> err:
				LogSeedSkip("Home", err.Value);
				break;
		}

		// Seed the Markdown formatting guide in the Help namespace. Like the home page,
		// CreateAsync rejects duplicate slugs, so re-seeding on restart is a no-op and
		// admin edits to the page survive.
		var guideResult = await wikiService.CreateAsync(
			title: "Markdown Guide",
			markdown: SeededWikiPages.MarkdownGuide,
			authorDbref: "#1",
			ns: WikiNamespace.Help,
			category: "general",
			sourceLocale: "en");
		switch (guideResult)
		{
			case WikiPage page:
				logger.LogInformation("Markdown Guide wiki page seeded (id={Id}).", page.Id);
				break;
			case Error<string> err:
				LogSeedSkip("Markdown Guide", err.Value);
				break;
		}

		// Seed the Dynamic Applications (Area 21) schema guide in the Help namespace, alongside
		// the Markdown Guide. Same idempotent CreateAsync contract: duplicate slugs are a no-op on
		// restart and admin edits to the page survive.
		var appSchemaResult = await wikiService.CreateAsync(
			title: "Application Schema Guide",
			markdown: SeededWikiPages.ApplicationSchemaGuide,
			authorDbref: "#1",
			ns: WikiNamespace.Help,
			category: "general",
			sourceLocale: "en");
		switch (appSchemaResult)
		{
			case WikiPage page:
				logger.LogInformation("Application Schema Guide wiki page seeded (id={Id}).", page.Id);
				break;
			case Error<string> err:
				LogSeedSkip("Application Schema Guide", err.Value);
				break;
		}
	}

	/// <summary>
	/// Logs a wiki-seed skip. A duplicate slug (the expected no-op when re-seeding on
	/// restart) is logged at Debug; any other failure is surfaced at Warning so genuine
	/// seeding problems are not silently masked as "already exists".
	/// </summary>
	private void LogSeedSkip(string page, string error)
	{
		if (error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
			logger.LogDebug("{Page} wiki page already exists; skipping seed.", page);
		else
			logger.LogWarning("{Page} wiki page could not be seeded: {Msg}", page, error);
	}

	public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StartedAsync(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var ct = _readinessCancellation.Token;
		_ready = Task.Run(() => PublishReadinessAsync(ct), CancellationToken.None);
		return Task.CompletedTask;
	}

	private async Task PublishReadinessAsync(CancellationToken ct)
	{
		try
		{
			if (consumers is not null) await consumers.WaitUntilReadyAsync(ct);
			while (!ct.IsCancellationRequested)
			{
				try
				{
					await messageBus.Publish(new MainProcessReadyMessage(DateTimeOffset.UtcNow, ServerVersion), ct);
					return;
				}
				catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
				{
					logger.LogWarning(ex, "Failed to publish engine readiness; retrying.");
				}
				await Task.Delay(TimeSpan.FromSeconds(2), ct);
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			logger.LogDebug("Engine readiness publishing cancelled.");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public async Task StoppingAsync(CancellationToken cancellationToken)
	{
		await _readinessCancellation.CancelAsync();
		try
		{
			await _ready.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			logger.LogDebug("Stopped waiting for readiness publishing during shutdown.");
		}
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

	public void Dispose()
	{
		_readinessCancellation.Cancel();
		_readinessCancellation.Dispose();
	}
}