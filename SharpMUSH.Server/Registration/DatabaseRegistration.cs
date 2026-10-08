using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Mcp;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.RateLimiting;
using SharpMUSH.Server.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Globalization;
using System.Threading.RateLimiting;
using OpenTelemetry.ResourceDetectors.Container;
using Quartz;
using Serilog;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Commands;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using SharpMUSH.Messaging.NATS;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;
namespace SharpMUSH.Server.Registration;

/// <summary>
/// The plugin catalog and the one concrete database provider, plus the environment variables that
/// size, sync and back up a Lightning world.
/// </summary>
/// <remarks>
/// The catalog is built here and nowhere else. It runs the single McMaster DLL-load pass, applies
/// every <c>IServiceRegistrar</c> straight into this collection, and is registered as a singleton so
/// the DB factory, <c>NatsBridgeService</c> and the post-build <c>PluginManager</c> all read the
/// same already-loaded set rather than loading any DLL a second time.
/// </remarks>
internal static class DatabaseRegistration
{
	/// <summary>Builds the plugin catalog, then puts the Lightning provider behind every store interface it serves.</summary>
	public static IServiceCollection AddSharpMushDatabase(
		this IServiceCollection services, IConfiguration configuration)
	{
		// PHASE 2a TWO-PHASE BOOT — build the plugin catalog ONCE, pre-build, before any service the
		// plugins might extend is registered. The catalog runs the single McMaster DLL-load pass, applies
		// every IServiceRegistrar straight into this IServiceCollection, and stashes the migration/flag/
		// bridge contributions. It is registered as a singleton so the DB factory (migrations + flags),
		// NatsBridgeService (bridge subscriptions), and the post-build PluginManager (commands/functions)
		// all read the same already-loaded set rather than loading any DLL a second time.
		using var pluginCatalogLoggerFactory = LoggerFactory.Create(b => b.AddSerilog(
			new LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger(), dispose: true));
		var pluginCatalog = Implementation.Services.PluginCatalog.Build(
			services, pluginCatalogLoggerFactory.CreateLogger<Implementation.Services.PluginCatalog>());
		services.AddSingleton(pluginCatalog);

		var pluginMigrationSources = pluginCatalog.MigrationSources;
		var pluginFlags = pluginCatalog.AllFlags;

		// Relations a provider cannot own - an object's location changes under other actors - resolve
		// through the Mediator's cached queries; the providers ask through this seam and know nothing
		// of the cache.
		services.AddSingleton<IObjectRelationLoader, Implementation.Services.MediatorObjectRelationLoader>();

		// Config-driven path/map-size so production picks a durable location while tests default to a
		// fresh temp directory per run. Resolution: SHARPMUSH_LIGHTNING_PATH env → appsettings
		// "Lightning:Path" → "lightning-data"; SHARPMUSH_LIGHTNING_MAPSIZE (bytes) → 64 GiB;
		// SHARPMUSH_LIGHTNING_SYNC (full|nometasync|periodic) → full; SHARPMUSH_LIGHTNING_FLUSH_MS → 1000.
		var lightningPath = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_PATH")
			?? configuration["Lightning:Path"]
			?? "lightning-data";
		var lightningMapSizeSetting = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_MAPSIZE");
		var lightningSyncSetting = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_SYNC");
		var lightningFlushSetting = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_FLUSH_MS");
		services.AddSingleton(new LightningWorldPath(lightningPath));
		var compactOnStart = string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_COMPACT_ON_START"), "true",
			StringComparison.OrdinalIgnoreCase);
		services.AddSingleton<LightningDatabase>(x =>
		{
			var dbLogger = x.GetRequiredService<ILogger<LightningDatabase>>();
			var password = x.GetRequiredService<IPasswordService>();
			var relations = x.GetRequiredService<IObjectRelationLoader>();
			var storeOptions = new LightningStoreOptions
			{
				Path = x.GetRequiredService<LightningWorldPath>().Value,
				MapSize = ResolveLightningMapSize(lightningMapSizeSetting, dbLogger),
				Sync = ResolveLightningSyncMode(lightningSyncSetting, dbLogger),
				FlushInterval = ResolveLightningFlushInterval(lightningFlushSetting, dbLogger)
			};

			// Before anything opens the world: finish or undo an interrupted compaction, then compact if asked.
			// A refused compaction is reported and the server starts on the world as it is.
			LightningCompaction.Recover(storeOptions.Path, dbLogger);
			if (compactOnStart && LightningCompaction.CompactInPlace(storeOptions, dbLogger) is Library.DiscriminatedUnions.Error<string> refused)
			{
				dbLogger.LogError("SHARPMUSH_LIGHTNING_COMPACT_ON_START is set, but the world was not compacted: {Reason}",
					refused.Value);
			}

			var db = new LightningDatabase(dbLogger, storeOptions, password, relations, pluginMigrationSources, pluginFlags);
			return db;
		});
		RegisterDatabaseProvider<LightningDatabase>(services);
		services.AddSingleton<SharpMUSH.Library.Plugins.Storage.ILightningStorageAccessor>(sp =>
			sp.GetRequiredService<LightningDatabase>());

		// World backup. SHARPMUSH_BACKUP_{PATH,KEEP,INTERVAL} say where copies go, how many stay and how
		// often one is taken; SHARPMUSH_BACKUP_PACKAGE_KEEP how many pre-package-operation copies stay;
		// SHARPMUSH_LIGHTNING_BACKUP_COMPACT turns off omitting free pages.
		var lightningCompactSetting = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_BACKUP_COMPACT");
		var compactBackups = !string.Equals(lightningCompactSetting, "false", StringComparison.OrdinalIgnoreCase);
		services.AddSingleton<IWorldBackupService>(sp => new LightningWorldBackupService(
			sp.GetRequiredService<SharpMUSH.Library.Plugins.Storage.ILightningStorageAccessor>(),
			ResolveBackupOptions(sp.GetRequiredService<LightningWorldPath>().Value, sp.GetRequiredService<ILogger<LightningDatabase>>()),
			compact: compactBackups,
			sp.GetRequiredService<ILogger<LightningWorldBackupService>>()));

		// Capacity reporting (@storage, the sharpmush.storage.* gauges), the audit log, and the provider's own
		// history kinds (wiki revisions, audit entries).
		services.AddSingleton<IStorageCapacityService>(sp => new LightningStorageCapacityService(
			sp.GetRequiredService<LightningDatabase>(), sp.GetRequiredService<IWorldBackupService>(), compactBackups));
		services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<LightningDatabase>().WikiHistory);
		services.AddSingleton<IAuditStore>(sp => sp.GetRequiredService<LightningDatabase>());
		services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<LightningDatabase>().AuditHistory);
		AddHistoryRetention(services);

		return services;
	}

	/// <summary>
	/// History retention over every registered <see cref="IHistoryStore"/> — the provider's and any a
	/// plugin adds. Its policy is read from <c>SHARPMUSH_HISTORY_*</c> once the stores are known, because
	/// the kinds name the settings; every default keeps everything.
	/// </summary>
	private static void AddHistoryRetention(IServiceCollection services)
		=> services.AddSingleton<IHistoryRetentionService>(sp =>
		{
			var stores = sp.GetServices<IHistoryStore>().ToArray();
			var logger = sp.GetRequiredService<ILogger<HistoryRetentionService>>();
			var options = HistoryRetentionOptions.Resolve(stores.Select(s => s.Kind), Environment.GetEnvironmentVariable,
				logger);
			return new HistoryRetentionService(stores, options, logger, TimeProvider.System);
		});

	private static void RegisterDatabaseProvider<TProvider>(IServiceCollection services)
		where TProvider : class, ISharpDatabase, IWikiStore, IPackageRegistryService,
		IApplicationRegistryService, ILayoutRegistryService, IRoleRegistryService
	{
		services.AddSingleton<ISharpDatabase>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IDatabaseLifecycle>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IObjectStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IFlagAndPowerStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<INavigationStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IAttributeStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IMailStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IMailAliasStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IExpandedDataStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IChannelStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IAccountStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IServerStateStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<ISessionRecordStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IReadMarkerStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IPageLogStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IFeedStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IWikiStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IWikiService, WikiStoreService>();

		// Portal subsystems the provider also backs: package registry, layout registry, RBAC roles.
		services.AddSingleton<IPackageRegistryService>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<ILayoutRegistryService>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IRoleRegistryService>(sp => sp.GetRequiredService<TProvider>());

		// Dynamic Application registry (Area 21), wrapped in a read-only overlay decorator so the
		// PluginCatalog's IApplicationSource contributions are unioned into reads while their plugins
		// are loaded (DB/built-in wins on a slug collision; plugin apps are not persisted and not
		// admin-editable). The provider is the decorator's inner.
		services.AddSingleton<IApplicationRegistryService>(sp =>
			new Implementation.Services.PluginApplicationRegistryDecorator(
				sp.GetRequiredService<TProvider>(),
				sp.GetRequiredService<Implementation.Services.PluginCatalog>(),
				sp.GetRequiredService<ILogger<Implementation.Services.PluginApplicationRegistryDecorator>>()));
	}

	/// <summary>LMDB's map size is a hard ceiling on the environment, so a typo in
	/// <c>SHARPMUSH_LIGHTNING_MAPSIZE</c> silently capping the world at the default would be the worst
	/// possible failure mode to keep quiet about. Falls back to 64 GiB, saying so.</summary>
	private static long ResolveLightningMapSize(string? setting, ILogger<LightningDatabase> logger)
	{
		const long defaultMapSize = 64L << 30;
		if (string.IsNullOrWhiteSpace(setting)) return defaultMapSize;
		if (long.TryParse(setting, out var parsed)) return parsed;

		logger.LogWarning(
			"SHARPMUSH_LIGHTNING_MAPSIZE is set to '{Setting}', which is not a byte count; using the default of {DefaultMapSize} bytes",
			setting, defaultMapSize);
		return defaultMapSize;
	}

	/// <summary>Unset means durable. A typo here must not quietly relax durability, and must not quietly
	/// keep it either when the operator meant to relax it — so the fallback is logged either way.</summary>
	private static LightningSyncMode ResolveLightningSyncMode(string? setting, ILogger<LightningDatabase> logger)
	{
		if (string.IsNullOrWhiteSpace(setting)) return LightningSyncMode.Full;
		if (LightningStoreOptions.TryParseSyncMode(setting, out var mode)) return mode;

		logger.LogWarning(
			"SHARPMUSH_LIGHTNING_SYNC is set to '{Setting}', which is not one of full, nometasync or periodic; using full",
			setting);
		return LightningSyncMode.Full;
	}

	/// <summary>Milliseconds between forced syncs under periodic mode; the most a power failure can lose.</summary>
	private static TimeSpan ResolveLightningFlushInterval(string? setting, ILogger<LightningDatabase> logger)
	{
		var defaultInterval = TimeSpan.FromSeconds(1);
		if (string.IsNullOrWhiteSpace(setting)) return defaultInterval;
		if (int.TryParse(setting, out var ms) && ms > 0) return TimeSpan.FromMilliseconds(ms);

		logger.LogWarning(
			"SHARPMUSH_LIGHTNING_FLUSH_MS is set to '{Setting}', which is not a positive millisecond count; using {DefaultMs} ms",
			setting, defaultInterval.TotalMilliseconds);
		return defaultInterval;
	}

	/// <summary>
	/// Where the copies go, how many are kept and how often one is taken.
	/// <paramref name="worldPath"/> only supplies the default root, beside the world, when
	/// <c>SHARPMUSH_BACKUP_PATH</c> is unset.
	/// </summary>
	private static WorldBackupOptions ResolveBackupOptions(string worldPath, Microsoft.Extensions.Logging.ILogger logger)
	{
		var configuredRoot = Environment.GetEnvironmentVariable("SHARPMUSH_BACKUP_PATH");

		var keepSetting = Environment.GetEnvironmentVariable("SHARPMUSH_BACKUP_KEEP");
		var intervalSetting = Environment.GetEnvironmentVariable("SHARPMUSH_BACKUP_INTERVAL");

		const int defaultKeep = 2;
		var keep = defaultKeep;
		if (!string.IsNullOrWhiteSpace(keepSetting))
		{
			// A typo must not silently mean "keep one" on a box sized for several, so it falls back loudly.
			if (int.TryParse(keepSetting, out var parsed) && parsed > 0)
			{
				keep = parsed;
			}
			else
			{
				logger.LogWarning(
					"SHARPMUSH_BACKUP_KEEP is set to '{Setting}', which is not a positive count; keeping {DefaultKeep}",
					keepSetting, defaultKeep);
			}
		}

		// Zero is a setting here, not a typo: it turns off the copy taken before a portal package operation.
		var packageKeepSetting = Environment.GetEnvironmentVariable("SHARPMUSH_BACKUP_PACKAGE_KEEP");
		const int defaultPackageKeep = 2;
		var packageKeep = defaultPackageKeep;
		if (!string.IsNullOrWhiteSpace(packageKeepSetting))
		{
			if (int.TryParse(packageKeepSetting, out var parsed) && parsed >= 0)
			{
				packageKeep = parsed;
			}
			else
			{
				logger.LogWarning(
					"SHARPMUSH_BACKUP_PACKAGE_KEEP is set to '{Setting}', which is not a count; keeping {DefaultKeep}",
					packageKeepSetting, defaultPackageKeep);
			}
		}

		// Unset means no scheduled backup, so an unreadable setting leaves scheduling off — and says so,
		// because the operator who set it is relying on it.
		if (!WorldBackupOptions.TryParseInterval(intervalSetting, out var interval))
		{
			logger.LogWarning(
				"SHARPMUSH_BACKUP_INTERVAL is set to '{Setting}', which is not an interval like 6h, 90m or a "
				+ "count of seconds; scheduled backups stay off",
				intervalSetting);
		}

		return new WorldBackupOptions
		{
			Root = string.IsNullOrWhiteSpace(configuredRoot)
				? WorldBackupOptions.DefaultRootFor(worldPath)
				: configuredRoot,
			Keep = keep,
			Interval = interval,
			PackageOperationKeep = packageKeep
		};
	}
}
