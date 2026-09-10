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
using SharpMUSH.Server.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using SurrealDb.Net;
using SurrealDb.Embedded.InMemory;
using System.Threading.RateLimiting;
using OpenTelemetry.ResourceDetectors.Container;
using Quartz;
using Serilog;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.SurrealDB;
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

namespace SharpMUSH.Server;

public class Startup(
	string colorFile,
	string natsUrl,
	DatabaseProvider databaseProvider = DatabaseProvider.Lightning)
{
	// Cache name for the dedicated compiled boolean-lock expression cache.
	// Must match the [FromKeyedServices] key used in BooleanExpressionParser.
	public const string CompiledExpressionsCacheName = "compiled-expressions";

	/// <summary>
	/// Exposes one concrete database provider under every interface it serves. The provider is a
	/// single instance registered as its own type; each interface here forwards to that instance,
	/// so the compiler checks that <typeparamref name="TProvider"/> implements every surface
	/// handed out, and a provider that drops one of them fails to build instead of failing to cast.
	/// </summary>
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
	/// Where the copies go, how many are kept and how often one is taken, for whichever provider can
	/// back itself up. Provider-neutral (<c>SHARPMUSH_BACKUP_*</c>) because three of them can, and the
	/// operator setting a retention count does not care which engine is underneath.
	/// <paramref name="worldPath"/> only supplies the default root.
	/// </summary>
	/// <param name="worldPath">
	/// The provider's own on-disk world, when it has one, used only to derive the default root. Null
	/// for a provider that keeps nothing locally — SurrealDB on a <c>mem://</c> endpoint.
	/// Those get no default: returns null unless <c>SHARPMUSH_BACKUP_PATH</c> names somewhere, because
	/// guessing puts the copies in the working directory, which on a container is not the mounted
	/// volume — backups that look like they are being taken and are gone at the next recreate.
	/// </param>
	/// <returns>Null when there is nowhere sensible to write, which the caller reports as unsupported.</returns>
	private static WorldBackupOptions? ResolveBackupOptions(string? worldPath, Microsoft.Extensions.Logging.ILogger logger)
	{
		var configuredRoot = Environment.GetEnvironmentVariable("SHARPMUSH_BACKUP_PATH");
		if (string.IsNullOrWhiteSpace(configuredRoot) && string.IsNullOrWhiteSpace(worldPath)) return null;

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
				? WorldBackupOptions.DefaultRootFor(worldPath!)
				: configuredRoot,
			Keep = keep,
			Interval = interval
		};
	}

	/// <summary>Why a provider that could otherwise back itself up is switched off.</summary>
	private const string NoBackupLocation =
		"can back itself up, but has no world directory to derive a location from; "
		+ "set SHARPMUSH_BACKUP_PATH to somewhere durable to enable it";

	public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
	{
		// Compress what we send. Only the Blazor _framework files arrived compressed before, because
		// UseBlazorFrameworkFiles serves pre-brotlied copies of those and nothing else was covered —
		// so a cold first visit pulled 15.0 MB, with Monaco's editor.api (3.67 MB), Mermaid (2.57 MB),
		// MudBlazor's CSS and mush-defs.json all going out as raw bytes.
		//
		// Fastest, not Optimal: this compresses on the fly, and the largest asset here is several
		// megabytes — paying maximum-ratio brotli per request would trade a download stall for a
		// server stall. Fastest still takes those files down by roughly an order of magnitude.
		// Responses that already carry a Content-Encoding (the pre-brotlied _framework files) are
		// skipped by the middleware, so nothing is compressed twice.
		services.AddResponseCompression(options =>
		{
			options.EnableForHttps = true;
			options.Providers.Add<BrotliCompressionProvider>();
			options.Providers.Add<GzipCompressionProvider>();
			options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
			[
				"application/javascript",
				"text/javascript",
				"application/json",
				"image/svg+xml",
				"application/manifest+json",
				"font/ttf",
				"application/wasm"
			]);
		});
		services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
		services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

		services.AddCors(options =>
		{
			// C-4: Read allowed origins from Cors:AllowedOrigins config array.
			// Falls back to dev-only wildcard (no AllowCredentials) when no origins are configured.
			// AllowCredentials() is required for SignalR WebSocket handshake, so it is only
			// enabled when specific origins are listed (or unconditionally in development, where
			// localhost is the only practical origin).
			var allowedOrigins = configuration
				.GetSection("Cors:AllowedOrigins")
				.Get<string[]>();

			options.AddDefaultPolicy(builder =>
			{
				if (allowedOrigins is { Length: > 0 })
				{
					builder.WithOrigins(allowedOrigins)
						.AllowAnyMethod()
						.AllowAnyHeader()
						.AllowCredentials();
				}
				else if (environment.IsDevelopment())
				{
					builder.SetIsOriginAllowed(_ => true)
						.AllowAnyMethod()
						.AllowAnyHeader()
						.AllowCredentials();
				}
				else
				{
					// Production with no origins configured: deny all cross-origin requests.
					builder.WithOrigins(Array.Empty<string>());
				}
			});
		});

		// Trusted client-IP resolution behind a reverse proxy (Caddy/Cloudflare in deploy/docker-compose.prod.yml):
		// the origin IP captured on account sessions (AuthController.ClientIp) and matched by sitelock host rules
		// must be the real client IP, not the proxy hop. The read happens INSIDE this delegate (not eagerly
		// here) so config sources appended after ConfigureServices runs — e.g. the test host's UseSetting
		// overrides — are still visible when options are first resolved (see the AddRateLimiter comment
		// further down for the same caveat).
		services.Configure<ForwardedHeadersOptions>(opts =>
		{
			opts.KnownIPNetworks.Clear();
			opts.KnownProxies.Clear();
			foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
				if (System.Net.IPAddress.TryParse(proxy, out var ip))
					opts.KnownProxies.Add(ip);
			foreach (var network in configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [])
				if (System.Net.IPNetwork.TryParse(network, out var ipNetwork))
					opts.KnownIPNetworks.Add(ipNetwork);

			// IMPORTANT (verified by ForwardedHeadersTests): ForwardedHeadersMiddleware treats an EMPTY
			// KnownProxies/KnownIPNetworks pair as "nothing to check against" and trusts EVERY remote —
			// the opposite of the spoof-safe default this config is supposed to give. So "no proxies
			// configured" must disable forwarded-header processing entirely rather than lean on an empty
			// allow-list to mean "trust nobody".
			opts.ForwardedHeaders = opts.KnownProxies.Count > 0 || opts.KnownIPNetworks.Count > 0
				? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
				: ForwardedHeaders.None;
		});

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

		if (databaseProvider == DatabaseProvider.SurrealDB)
		{
			// Config-driven endpoint so production persists to disk (RocksDB) while tests stay in-memory.
			// Resolution: SHARPMUSH_SURREALDB_ENDPOINT env → appsettings "SurrealDb:Endpoint" → file-backed default.
			// A pure mem:// store loses ALL data on restart, so production must default to a durable engine.
			var surrealEndpoint = Environment.GetEnvironmentVariable("SHARPMUSH_SURREALDB_ENDPOINT")
				?? configuration["SurrealDb:Endpoint"]
				?? "rocksdb://surrealdb-data";
			// Register both embedded engines; the endpoint scheme selects which the live client uses, and the
			// migration staging client (always mem://) needs the in-memory engine present regardless.
			services.AddSurreal($"Endpoint={surrealEndpoint};Namespace=sharpmush;Database=world")
				.AddInMemoryProvider()
				.AddRocksDbProvider();
			services.AddSingleton<SurrealDatabase>(x =>
			{
				var dbLogger = x.GetRequiredService<ILogger<SurrealDatabase>>();
				var surrealClient = x.GetRequiredService<ISurrealDbClient>();
				surrealClient.Connect().ConfigureAwait(false).GetAwaiter().GetResult();
				var password = x.GetRequiredService<IPasswordService>();
				var db = new SurrealDatabase(dbLogger, surrealClient, password,
					x.GetRequiredService<IObjectRelationLoader>(), pluginMigrationSources, pluginFlags);
				return db;
			});
			RegisterDatabaseProvider<SurrealDatabase>(services);
			services.AddSingleton<SharpMUSH.Library.Plugins.Storage.ISurrealStorageAccessor>(sp =>
				sp.GetRequiredService<SurrealDatabase>());

			// The default root is derived from the endpoint's own path when it is file-backed, so the
			// export lands beside the world. A mem:// endpoint has no world on disk and gets no default:
			// there is nothing durable to sit beside, and the working directory is the wrong guess.
			var surrealWorldPath = surrealEndpoint.StartsWith("rocksdb://", StringComparison.OrdinalIgnoreCase)
				? surrealEndpoint["rocksdb://".Length..]
				: null;
			services.AddSingleton<IWorldBackupService>(sp =>
			{
				var options = ResolveBackupOptions(surrealWorldPath, sp.GetRequiredService<ILogger<SurrealDatabase>>());
				return options is null
					? new UnsupportedWorldBackupService("surrealdb", NoBackupLocation)
					: new SurrealWorldBackupService(sp.GetRequiredService<ISurrealDbClient>(), options,
						sp.GetRequiredService<ILogger<SurrealWorldBackupService>>());
			});
		}
		else
		{
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
			services.AddSingleton<LightningDatabase>(x =>
			{
				var dbLogger = x.GetRequiredService<ILogger<LightningDatabase>>();
				var password = x.GetRequiredService<IPasswordService>();
				var relations = x.GetRequiredService<IObjectRelationLoader>();
				var db = new LightningDatabase(dbLogger,
					new LightningStoreOptions
					{
						Path = lightningPath,
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

			// World backup. SHARPMUSH_BACKUP_{PATH,KEEP,INTERVAL} are shared with the other providers that
			// can back themselves up; compaction is Lightning's alone, because only a page-level copy has
			// free pages to omit.
			var lightningCompactSetting = Environment.GetEnvironmentVariable("SHARPMUSH_LIGHTNING_BACKUP_COMPACT");
			services.AddSingleton<IWorldBackupService>(sp => new LightningWorldBackupService(
				sp.GetRequiredService<SharpMUSH.Library.Plugins.Storage.ILightningStorageAccessor>(),
				// Never null: Lightning always has a world directory to name the default root after.
				ResolveBackupOptions(lightningPath, sp.GetRequiredService<ILogger<LightningDatabase>>())!,
				compact: !string.Equals(lightningCompactSetting, "false", StringComparison.OrdinalIgnoreCase),
				sp.GetRequiredService<ILogger<LightningWorldBackupService>>()));
		}
		services.AddSingleton<PasswordHasher<string>, PasswordHasher<string>>(_ => new PasswordHasher<string>()
		/*
		 * PennMUSH Password Compatibility - IMPLEMENTED
		 *
		 * SharpMUSH uses PBKDF2 with HMAC-SHA512, 128-bit salt, 256-bit subkey, 100000 iterations
		 * for new passwords.
		 *
		 * PennMUSH uses SHA1 in password_hash, stored as: V:ALGO:HASH:TIMESTAMP
		 * - V: Version number (Currently 2)
		 * - ALGO: Digest algorithm (Default is SHA1)
		 * - HASH: Salted hash (first 2 chars are salt prepended to plaintext before hashing)
		 * - TIMESTAMP: Unix timestamp when password was set
		 *
		 * Salt characters: abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789
		 *
		 * The PasswordService now supports both formats:
		 * - Verification: Detects PennMUSH format and uses SHA1/SHA256 verification as needed
		 * - New passwords: Always use modern PBKDF2 (more secure)
		 *
		 * Users with imported PennMUSH passwords should reset their passwords for better security,
		 * but can still log in with their old passwords until they do.
		 */
		);
		services.AddSingleton<IPasswordService, PasswordService>();
		services.AddSingleton<IPermissionService, PermissionService>();
		services.AddSingleton<ITelemetryService, TelemetryService>();

		services.AddSingleton<IConnectionStateStore>(sp =>
		{
			var logger = sp.GetRequiredService<ILogger<NatsConnectionStateStore>>();
			return NatsConnectionStateStore.CreateAsync(natsUrl, logger).GetAwaiter().GetResult();
		});

		services.AddSingleton<ILocalizationService, LocalizationService>();
		services.AddSingleton<INotifyService, NotifyService>();
		services.AddSingleton<ILocateService, LocateService>();
		services.AddSingleton<IMoveService, MoveService>();
		services.AddSingleton<IObjectDestructionService, ObjectDestructionService>();
		services.AddSingleton<IExpandedObjectDataService, ExpandedObjectDataService>();
		services.AddSingleton<IAttributeService, AttributeService>();
		services.AddSingleton<IEngineCommandInvoker, EngineCommandInvoker>();
		services.AddSingleton<IManipulateSharpObjectService, ManipulateSharpObjectService>();
		services.AddSingleton<ITaskScheduler, TaskScheduler>();
		services.AddSingleton<IQueueControlService, QueueControlService>();
		services.AddSingleton<IConnectionService, ConnectionService>();
		services.AddSingleton<IOttStore, InMemoryOttStore>();
		services.AddSingleton<HubConnectionRegistry>();
		services.AddSingleton<IAccountSessionStore, DatabaseAccountSessionStore>();
		services.AddSingleton<IAccountService, AccountService>();
		// Unconditional (not gated on JWT config) — AuthController's account-login/register and
		// AdminAccountsController's Wizard gate need it even when JWT auth isn't configured.
		services.AddSingleton<AccountClaimsService>();
		// Kept separate from AccountClaimsService so AccountService — which computes nothing but
		// must invalidate whenever it links or unlinks a character — can depend on it without
		// closing a cycle back through IAccountService. Library stays off Server.
		services.AddSingleton<IAccountClaimsInvalidator, AccountClaimsInvalidator>();
		// Task 15: gates AuthController/SetupController/GameHub on sitelock rules (!connect/!create/!guest).
		services.AddSingleton<SitelockGuard>();
		services.AddSingleton<BanEnforcementService>();
		// Library-layer call sites (AccountService, SitelockController) depend on IBanEnforcer, not
		// the concrete Server-layer BanEnforcementService, so Library stays off Server.
		services.AddSingleton<IBanEnforcer>(sp => sp.GetRequiredService<BanEnforcementService>());
		services.AddHostedService<BootstrapService>();
		services.AddSingleton<SetupService>();
		services.AddHostedService<RoleSeedService>();
		services.AddSingleton<ISqlService, SqlService>();
		services.AddSingleton<IPackageManifestService, PackageManifestService>();
		services.AddSingleton<IPackagePlanService, PackagePlanService>();
		// Parser-layer runner for package AINSTALL/AUPDATE softcode; required by PackageInstallService.
		services.AddSingleton<IPackageLifecycleRunner, SharpMUSH.Implementation.Services.PackageLifecycleRunner>();
		// Phase-4 managed-package (compiled C# plugin DLL) installer + its server-side trust allow-list.
		// The allow-list is read from the "ManagedPackages" config section (AllowAll / AllowList) and is
		// the standing half of the trust gate; the per-apply allow_managed_code flag is the other half.
		services.AddSingleton(_ =>
		{
			var section = configuration.GetSection("ManagedPackages");
			var allowAll = section.GetValue("AllowAll", false);
			var allowList = section.GetSection("AllowList").Get<string[]>() ?? [];
			return new ManagedPackageTrustOptions(allowAll, allowList);
		});
		services.AddSingleton<IManagedPackageInstaller>(sp => new ManagedPackageInstaller(
			sp.GetRequiredService<IPluginManager>(),
			sp.GetRequiredService<ManagedPackageTrustOptions>(),
			sp.GetRequiredService<ILogger<ManagedPackageInstaller>>()));
		// Serves a managed plugin's compiled UI assembly bytes to the WASM client, re-verifying them against
		// the Phase-4 install-time SHA-256 sidecar before serving. The PluginsUiController gates it on
		// allow_browser_code; this provider enforces the hash/traversal guards regardless.
		services.AddSingleton<IPluginUiAssemblyProvider>(sp =>
			new FileSystemPluginUiAssemblyProvider(
				sp.GetRequiredService<ILogger<FileSystemPluginUiAssemblyProvider>>()));
		services.AddSingleton<IPackageInstallService, PackageInstallService>();
		services.AddSingleton<IPackageAuthoringService, PackageAuthoringService>();
		services.AddSingleton<IPackageSourceService>(sp =>
			new Services.GitPackageSourceService(sp.GetRequiredService<IPackageManifestService>()));
		services.AddSingleton<ICommunicationService, CommunicationService>();
		services.AddSingleton<ILockService, LockService>();
		services.AddSingleton<IGameBroadcastService, GameBroadcastService>();
		services.AddSingleton<IConnectionAnnounceService, ConnectionAnnounceService>();
		services.AddSingleton<IBooleanExpressionParser, BooleanExpressionParser>();
		services.AddSingleton<ICommandDiscoveryService, CommandDiscoveryService>();
		services.AddSingleton<ISortService, SortService>();
		services.AddSingleton<IHookService, HookService>();
		services.AddSingleton<IEventService, EventService>();
		// Inbound HTTP: run http_handler <METHOD> attributes as commands (see help sharphttp).
		services.AddSingleton<IHttpOutputCapture, HttpOutputCapture>();
		services.AddSingleton<IHttpHandlerCommandDispatcher, HttpHandlerCommandService>();
		services.AddSingleton<IWarningService, WarningService>();
		services.AddSingleton<IChannelBufferService, InMemoryChannelBufferService>();
		services.AddSingleton<IListenPatternMatcher, ListenPatternMatcher>();
		services.AddSingleton<IListenerRoutingService, ListenerRoutingService>();
		services.AddSingleton<PennMUSHDatabaseParser>();
		services.AddSingleton<IPennMUSHDatabaseConverter, PennMUSHDatabaseConverter>();

		// Wiki subsystem — IWikiService is the active database provider (see RegisterDatabaseProvider).
		services.AddSingleton<WikiMarkdigPipeline>();

		// Locale fallback rules (pure) and the one localized-read service every reader path goes through.
		services.AddSingleton<IWikiLocaleResolver, WikiLocaleResolver>();
		services.AddSingleton<IWikiLocalizationService, WikiLocalizationService>();

		// Package, application, layout and role registries are the active database provider too
		// (see RegisterDatabaseProvider).
		services.AddSingleton<IPermissionResolver, PermissionResolver>();
		services.AddSingleton<IAdministrativeCapabilityService, AdministrativeCapabilityService>();
		services.AddSingleton<SharpMUSH.Library.Services.RecurringJobs.IRecurringJobService, SharpMUSH.Library.Services.RecurringJobs.RecurringJobService>();
		services.AddSingleton<SharpMUSH.Library.Services.Snapshots.IObjectSnapshotService, SharpMUSH.Library.Services.Snapshots.ObjectSnapshotService>();
		services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, FreshPermissionClaimsTransformation>();
		services.AddSingleton<IWikiAssetService, Server.Services.FileSystemWikiAssetService>();

		// Scene subsystem — ISceneService is NO LONGER implemented by core providers. It is registered by
		// the Scene plugin's IServiceRegistrar (ScenePlugin.RegisterServices -> services.AddSceneSystem),
		// which keys per-provider storage over the host-shared storage accessors registered above and wraps
		// it with any registered behaviors. Removing the plugin leaves core with no scene storage.

		// Pre-render cache for bot-facing static HTML (backed by the shared IMemoryCache from FusionCache setup).
		services.AddMemoryCache();
		services.AddSingleton<Server.Services.IPrerenderCacheService, Server.Services.PrerenderCacheService>();

		services.AddSingleton<ITextFileService, Implementation.Services.TextFileService>();
		services.AddSingleton<ILocalizedTextFileService, Implementation.Services.LocalizedTextFileService>();
		// Shared by the in-game HELP/NEWS/AHELP commands and the portal's /api/help endpoints, so a
		// topic resolves identically at a telnet prompt and in a browser.
		services.AddSingleton<IHelpTopicResolver, Implementation.Services.HelpTopicResolver>();

		services.AddSingleton<ILibraryProvider<FunctionDefinition>, Functions>();
		services.AddSingleton<ILibraryProvider<CommandDefinition>, Commands>();
		// In-memory registry of global user-defined functions (@function). Not persisted:
		// durability comes from re-running @function on boot via the @STARTUP attribute pass.
		services.AddSingleton<IUserDefinedFunctionService, UserDefinedFunctionService>();
		services.AddSingleton(x => x.GetService<ILibraryProvider<FunctionDefinition>>()!.Get());
		services.AddSingleton(x => x.GetService<ILibraryProvider<CommandDefinition>>()!.Get());

		// Generic "plugins changed" notifier: after a plugin unload/reload, the PluginManager fires this to
		// broadcast ReceivePluginsChanged to every connected portal client, which forces a hard browser refresh
		// (the only way to reclaim a compiled component assembly loaded into the WASM runtime). Registered
		// before the PluginManager so its optional IPluginChangeNotifier ctor param resolves.
		services.AddSingleton<IPluginChangeNotifier, Server.Services.SignalRPluginChangeNotifier>();
		// C# plugin loader: discovers plugins/ DLLs at boot and registers their [SharpCommand]/[SharpFunction]
		// into the live command/function libraries with IsSystem=true (see PluginBootstrapService below).
		services.AddSingleton<IPluginManager, Implementation.Services.PluginManager>();
		// Phase 2b engine-extension hooks: the dispatcher the engine consults at its command/object seams,
		// reading the hook buckets the PluginCatalog collected. (Connection hooks are wired as
		// IConnectionService.ListenState listeners by PluginBootstrapService.)
		services.AddSingleton<IPluginHookDispatcher, Implementation.Services.PluginHookDispatcher>();

		services.AddSingleton<IOptionsFactory<SharpMUSHOptions>, OptionsService>();
		services.AddSingleton<IOptionsFactory<ColorsOptions>, ReadColorsOptionsFactory>();
		services.AddSingleton<ConfigurationReloadService>();
		services.AddSingleton<IOptionsChangeTokenSource<SharpMUSHOptions>>(sp =>
			sp.GetRequiredService<ConfigurationReloadService>());
		services.AddSingleton<ObjectVersions>();
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(QueryCachingBehavior<,>));
		services.AddSingleton(typeof(IStreamPipelineBehavior<,>), typeof(StreamQueryCachingBehavior<,>));
		services.AddSingleton<IMUSHCodeParser, MUSHCodeParser>();
		// Lazy<T> for the services only reachable through a cycle: the lock service owns the expression
		// parser, and the locate and attribute services reach the lock service through permissions.
		services.AddTransient(typeof(Lazy<>), typeof(Library.Services.LazyService<>));
		services.AddSingleton<ILockEvaluationServices, Implementation.Services.LockEvaluationServices>();
		// Shared MUSH code intelligence — the single source of truth behind both the
		// Language Server (for editors) and the in-server MCP tools (for agents/tooling).
		services.AddSingleton<IMushCodeAnalyzer, MushCodeAnalyzer>();
		services.AddSingleton<IValidateService, ValidateService>();
		services.AddKeyedSingleton(nameof(colorFile), colorFile);
		services.AddOptions<SharpMUSHOptions>().ValidateOnStart();
		// ValidateSharpOptions, not the generated validator directly: it delegates to the generated one and
		// adds the checks the generator cannot express (currently Wiki.DefaultLocale being a real culture).
		// Registering the generated validator here would silently skip those.
		//
		// Singleton, because the only thing that resolves it is the singleton OptionsService — see its
		// remarks for why that factory runs the validators itself. A scoped validator is a captive
		// dependency of a singleton factory, and nothing noticed while nothing ran it.
		services.AddSingleton<IValidateOptions<SharpMUSHOptions>, ValidateSharpOptions>();
		services.AddOptions<ColorsOptions>().ValidateOnStart();
		services.AddSingleton<IOptionsWrapper<SharpMUSHOptions>, Library.Services.OptionsWrapper<SharpMUSHOptions>>();
		services.AddSingleton<IOptionsWrapper<ColorsOptions>, Library.Services.OptionsWrapper<ColorsOptions>>();
		services.AddHttpClient();
		services.AddHttpClient("api")
			.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
			{
				AutomaticDecompression = System.Net.DecompressionMethods.GZip
																 | System.Net.DecompressionMethods.Deflate
																 | System.Net.DecompressionMethods.Brotli
			});
		services.AddMediator();

		services.AddLogging(logging =>
		{
			logging.ClearProviders();

			var loggerConfig = new LoggerConfiguration()
				.ReadFrom.Configuration(configuration);

			logging.AddSerilog(loggerConfig.CreateLogger());
		});

		services.AddNatsMainProcessMessaging(
			options => { options.Url = natsUrl; },
			x =>
			{
				x.AddConsumer<Consumers.TelnetInputConsumer, TelnetInputMessage>();
				x.AddConsumer<Consumers.WebSocketInputConsumer, WebSocketInputMessage>();
				x.AddConsumer<Consumers.GMCPSignalConsumer, GMCPSignalMessage>();
				x.AddConsumer<Consumers.MSDPUpdateConsumer, MSDPUpdateMessage>();
				x.AddConsumer<Consumers.NAWSUpdateConsumer, NAWSUpdateMessage>();
				x.AddConsumer<Consumers.ConnectionEstablishedConsumer, ConnectionEstablishedMessage>();
				x.AddConsumer<Consumers.ConnectionClosedConsumer, ConnectionClosedMessage>();
				x.AddConsumer<Consumers.SessionResumeConsumer, SessionResumeRequestMessage>();
				x.AddConsumer<Consumers.PuebloNegotiatedConsumer, PuebloNegotiatedMessage>();
				x.AddConsumer<Consumers.MxpNegotiatedConsumer, MxpNegotiatedMessage>();
				x.AddConsumer<Consumers.TerminalTypeNegotiatedConsumer, TerminalTypeNegotiatedMessage>();
				x.AddConsumer<Consumers.TelnetNegotiatedConsumer, TelnetNegotiatedMessage>();
			});

		// The engine cache. Its own bounded memory cache rather than the registered one (which the
		// prerender service shares), and the per-query profiles applied by the caching behaviours -
		// see CacheEntryProfile for the fail-safe rule. The registered default is the Tagged profile,
		// the one that can never serve stale: an ad-hoc caller that only sets a duration (the account
		// claims cache, invalidated by tag when a ban or role change lands) inherits everything else
		// from here, and must not inherit fail-safe. Only the caching behaviour hands out the Object
		// profile, and only to queries whose invalidation is by key.
		services.AddFusionCache()
			.TryWithAutoSetup()
			.WithMemoryCache(_ => new MemoryCache(new MemoryCacheOptions
			{
				SizeLimit = CacheEntryProfiles.MemoryCacheSizeLimit,
				CompactionPercentage = CacheEntryProfiles.MemoryCacheCompactionPercentage,
			}))
			.WithDefaultEntryOptions(CacheEntryProfiles.Tagged);

		// Dedicated cache for compiled boolean-lock expressions.
		// Uses a size-limited memory cache (max 1024 entries, 25% compaction) so rarely-used
		// entries are evicted first under memory pressure while hot entries stay resident.
		services.AddFusionCache(CompiledExpressionsCacheName)
			.WithMemoryCache(_ => new MemoryCache(new MemoryCacheOptions
			{
				SizeLimit = 1024,
				CompactionPercentage = 0.25,
			}))
			.AsKeyedServiceByCacheName();
		services.AddQuartz(x =>
		{
			x.UseInMemoryStore();
			// Serial execution ensures FIFO queue ordering, matching PennMUSH behavior
			// where the command queue processes one entry at a time.
			x.UseDefaultThreadPool(tp => tp.MaxConcurrency = 1);
		});
		// Single web credential: the account-session token. AccountSession is the default
		// scheme in production; DebugAuth remains the dev default (auto-admin). MushBasic
		// stays as the opt-in MCP scheme.
		var authBuilder = environment.IsDevelopment()
			? services.AddAuthentication(DebugAuthenticationHandler.SchemeName)
				.AddScheme<AuthenticationSchemeOptions, DebugAuthenticationHandler>(
					DebugAuthenticationHandler.SchemeName, _ => { })
			: services.AddAuthentication(AccountSessionAuthenticationHandler.SchemeName);

		authBuilder.AddScheme<AuthenticationSchemeOptions, AccountSessionAuthenticationHandler>(
			AccountSessionAuthenticationHandler.SchemeName, _ => { });

		services.AddAuthentication()
			.AddScheme<AuthenticationSchemeOptions, MushBasicAuthenticationHandler>(
				MushBasicAuthenticationHandler.SchemeName, _ => { });

		// In-server MCP (Model Context Protocol): exposes the shared MUSH code intelligence as
		// tools over Streamable HTTP. Services are always registered; the endpoint is only
		// mapped when Mcp:Enabled is true (see Program.MapMcp), so a disabled MCP returns 404.
		services.Configure<McpOptions>(configuration.GetSection(McpOptions.Section));
		services.AddSingleton<McpDocumentStore>();
		services.AddMcpServer()
			.WithHttpTransport(mcpTransport => mcpTransport.Stateless = true)
			.WithTools<MushTools>();

		services.AddSingleton<IRoleDerivationService, RoleDerivationService>();

		services.AddSignalR();

		// URL-segment strategy: /api/v1/... and /api/v2/...
		// Default version: 1.0 so existing unversioned controllers keep working.
		// Deprecated versions are announced via the api-deprecated-versions response header.
		services.AddApiVersioning(options =>
		{
			options.DefaultApiVersion = new ApiVersion(1, 0);
			options.AssumeDefaultVersionWhenUnspecified = true;
			options.ReportApiVersions = true;
			options.ApiVersionReader = ApiVersionReader.Combine(
				new UrlSegmentApiVersionReader(),
				new HeaderApiVersionReader("x-api-version"));
		}).AddMvc();

		// Named "public-api" policy: fixed window, 30 req/min per client IP,
		// queue depth 5.  Auth endpoints opt in via [EnableRateLimiting("public-api")].
		// Limits are configuration-driven (defaults below match the historical hardcoded
		// values) so the test host can raise them without touching production behavior.
		// NOTE: the config reads live INSIDE the AddRateLimiter delegate on purpose — the
		// delegate runs lazily at options resolution (after the host is fully built), so
		// configuration sources appended late (e.g. the test host's in-memory overrides)
		// are visible. An eager read at ConfigureServices time only ever sees the defaults
		// under WebApplicationFactory-style test hosts.
		services.AddRateLimiter(opts =>
		{
			opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			opts.AddFixedWindowLimiter("public-api", limiterOpts =>
			{
				limiterOpts.PermitLimit = configuration.GetValue("RateLimiting:PublicApi:PermitLimit", 30);
				limiterOpts.Window = TimeSpan.FromSeconds(configuration.GetValue("RateLimiting:PublicApi:WindowSeconds", 60));
				limiterOpts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
				limiterOpts.QueueLimit = configuration.GetValue("RateLimiting:PublicApi:QueueLimit", 5);
			});

			// "mcp" policy: partitioned per client IP so one source can't brute-force the
			// character+password auth on /mcp, while a legitimate agent (single IP) still gets
			// generous tool-call throughput. Partitioning (unlike the global "public-api" limiter)
			// keeps one caller's bursts from throttling everyone else.
			opts.AddPolicy("mcp", httpContext =>
				RateLimitPartition.GetFixedWindowLimiter(
					partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
					_ => new FixedWindowRateLimiterOptions
					{
						PermitLimit = 300,
						Window = TimeSpan.FromMinutes(1),
						QueueLimit = 0
					}));
		});

		services.AddProblemDetails();
		services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

		services.AddAuthorization();
		// Permission-policy plumbing: resolves [Authorize(Policy = PortalPermission.X)] gates against
		// the per-scope "perm" claims carried in the JWT.
		services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
		services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
		services.AddRazorPages();
		services.AddControllers();
		services.AddQuartzHostedService();
		services.AddHostedService<StartupHandler>();
		// Load C# plugins before softcode packages/startup attributes run, so plugin commands/functions
		// are present in the libraries when later bootstrap stages execute.
		services.AddHostedService<Services.PluginBootstrapService>();
		services.AddHostedService<Services.DefaultPackagesBootstrapService>();
		services.AddHostedService<Services.DefaultApplicationsBootstrapService>();
		// Run @STARTUP on all objects at boot — registered after the other bootstrap services so
		// any objects/attributes they seed already exist. Re-establishes in-memory @function regs.
		services.AddHostedService<Services.StartupAttributeBootstrapService>();
		services.AddHostedService<NatsBridgeService>();
		services.AddHostedService<Services.ConnectionReconciliationService>();
		services.AddHostedService<Services.ConnectionLoggingService>();
		services.AddHostedService<Services.HealthMonitoringService>();
		services.AddHostedService<Services.ScheduledTaskManagementService>();
		services.AddHostedService<Services.WarningCheckService>();
		services.AddHostedService<Services.WorldBackupScheduleService>();
		services.AddHostedService<Services.RecurringJobRunner>();
		services.AddHostedService<Services.PennMUSHDatabaseConversionService>();

		// Configure OpenTelemetry Metrics with GKE/Kubernetes-aware resource detection
		// Prometheus exporter is compatible with both GKE Managed Prometheus and standard Prometheus
		var isGKE = LoggingConfiguration.IsRunningInGKE();
		var isK8s = LoggingConfiguration.IsRunningInKubernetes();

		services.AddOpenTelemetry()
			.ConfigureResource(resource =>
			{
				resource.AddService(
					serviceName: "sharpmush-server",
					serviceVersion: "1.0.0",
					serviceInstanceId: Environment.MachineName);

				if (isK8s)
				{
					resource.AddDetector(new ContainerResourceDetector());
				}

				// Add GKE-specific attributes for Google Cloud Monitoring compatibility
				if (isGKE)
				{
					var projectId = LoggingConfiguration.GetGoogleCloudProjectId();
					if (!string.IsNullOrEmpty(projectId))
					{
						resource.AddAttributes(new[]
						{
							new KeyValuePair<string, object>("cloud.provider", "gcp"),
							new KeyValuePair<string, object>("cloud.platform", "gcp_kubernetes_engine"),
							new KeyValuePair<string, object>("gcp.project.id", projectId)
						});
					}
				}
			})
			.WithMetrics(metrics => metrics
				.AddMeter("SharpMUSH")
				.AddRuntimeInstrumentation()
				.AddAspNetCoreInstrumentation()
				.AddPrometheusExporter());
	}
}
