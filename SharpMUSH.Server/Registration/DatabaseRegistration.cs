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
		services.AddSingleton<LightningDatabase>(x =>
		{
			var dbLogger = x.GetRequiredService<ILogger<LightningDatabase>>();
			var password = x.GetRequiredService<IPasswordService>();
			var relations = x.GetRequiredService<IObjectRelationLoader>();
			var db = new LightningDatabase(dbLogger,
				new LightningStoreOptions
				{
					Path = x.GetRequiredService<LightningWorldPath>().Value,
					MapSize = ResolveLightningMapSize(lightningMapSizeSetting, dbLogger),
					Sync = ResolveLightningSyncMode(lightningSyncSetting, dbLogger),
					FlushInterval = ResolveLightningFlushInterval(lightningFlushSetting, dbLogger)
				},
				password, relations, pluginMigrationSources, pluginFlags);
			return db;
		});
		RegisterDatabaseProvider<LightningDatabase>(services);
		services.AddSingleton<SharpMUSH.Library.Plugins.Storage.ILightningStorageAccessor>(sp =>
			sp.GetRequiredService<LightningDatabase>());

		// World backup. SHARPMUSH_BACKUP_{PATH,KEEP,INTERVAL} say where copies go, how many stay and how
		// often one is taken; SHARPMUSH_LIGHTNING_BACKUP_COMPACT turns off omitting free pages.
		var lightningCompactSetting = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_BACKUP_COMPACT");
		services.AddSingleton<IWorldBackupService>(sp => new LightningWorldBackupService(
			sp.GetRequiredService<SharpMUSH.Library.Plugins.Storage.ILightningStorageAccessor>(),
			ResolveBackupOptions(sp.GetRequiredService<LightningWorldPath>().Value, sp.GetRequiredService<ILogger<LightningDatabase>>()),
			compact: !string.Equals(lightningCompactSetting, "false", StringComparison.OrdinalIgnoreCase),
			sp.GetRequiredService<ILogger<LightningWorldBackupService>>()));

		return services;
	}

	private static void RegisterDatabaseProvider<TProvider>(IServiceCollection services)
		where TProvider : class, ISharpDatabase, IWikiService, IPackageRegistryService,
		IApplicationRegistryService, ILayoutRegistryService, IRoleRegistryService
	{
		services.AddSingleton<ISharpDatabase>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IDatabaseLifecycle>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IObjectStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IFlagAndPowerStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<INavigationStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IAttributeStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IMailStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IExpandedDataStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IChannelStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IAccountStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<IServerStateStore>(sp => sp.GetRequiredService<TProvider>());
		services.AddSingleton<ISessionRecordStore>(sp => sp.GetRequiredService<TProvider>());

		// Portal subsystems the provider also backs: wiki, package registry, layout registry, RBAC roles.
		services.AddSingleton<IWikiService>(sp => sp.GetRequiredService<TProvider>());
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
			Interval = interval
		};
	}
}
