using Asp.Versioning;
using Core.Arango;
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
using Neo4j.Driver;
using SharpMUSH.CodeAnalysis;
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
using SharpMUSH.Database.ArangoDB;
using SharpMUSH.Database.Memgraph;
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
using SharpMUSH.Messaging.NATS;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Server;

public class Startup(
	ArangoConfiguration? arangoConfig,
	string colorFile,
	string natsUrl,
	DatabaseProvider databaseProvider = DatabaseProvider.ArangoDB,
	string? memgraphUri = null)
{
// Cache name for the dedicated compiled boolean-lock expression cache.
// Must match the [FromKeyedServices] key used in BooleanExpressionParser.
	public const string CompiledExpressionsCacheName = "compiled-expressions";

	public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
	{
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

		if (databaseProvider == DatabaseProvider.Memgraph)
		{
			services.AddSingleton<IDriver>(_ =>
				GraphDatabase.Driver(
					memgraphUri ?? "bolt://localhost:7687",
					o => o.WithEncryptionLevel(EncryptionLevel.None)));
			services.AddSingleton<ISharpDatabase, MemgraphDatabase>(x =>
			{
				var dbLogger = x.GetRequiredService<ILogger<MemgraphDatabase>>();
				var neo4JDriver = x.GetRequiredService<IDriver>();
				var password = x.GetRequiredService<IPasswordService>();
				var db = new MemgraphDatabase(dbLogger, neo4JDriver, password, pluginMigrationSources, pluginFlags);
				db.Migrate().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
				return db;
			});
			// Host-shared storage accessor for storage plugins (e.g. the Scene plugin). Generic seam over the
			// active provider's connection; carries no subsystem concept.
			services.AddSingleton<SharpMUSH.Library.Plugins.Storage.IMemgraphStorageAccessor>(sp =>
				(SharpMUSH.Library.Plugins.Storage.IMemgraphStorageAccessor)sp.GetRequiredService<ISharpDatabase>());
		}
		else if (databaseProvider == DatabaseProvider.SurrealDB)
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
			services.AddSingleton<ISharpDatabase, SurrealDatabase>(x =>
			{
				var dbLogger = x.GetRequiredService<ILogger<SurrealDatabase>>();
				var surrealClient = x.GetRequiredService<ISurrealDbClient>();
				surrealClient.Connect().ConfigureAwait(false).GetAwaiter().GetResult();
				var password = x.GetRequiredService<IPasswordService>();
				var db = new SurrealDatabase(dbLogger, surrealClient, password, pluginMigrationSources, pluginFlags);
				db.Migrate().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
				return db;
			});
			services.AddSingleton<SharpMUSH.Library.Plugins.Storage.ISurrealStorageAccessor>(sp =>
				(SharpMUSH.Library.Plugins.Storage.ISurrealStorageAccessor)sp.GetRequiredService<ISharpDatabase>());
		}
		else
		{
			services.AddSingleton<ISharpDatabase, ArangoDatabase>(x =>
			{
				var dbLogger = x.GetRequiredService<ILogger<ArangoDatabase>>();
				var context = x.GetRequiredService<IArangoContext>();
				var handle = x.GetRequiredService<ArangoHandle>();
				var mediator = x.GetRequiredService<IMediator>();
				var password = x.GetRequiredService<IPasswordService>();
				var db = new ArangoDatabase(dbLogger, context, handle, mediator, password, pluginMigrationSources, pluginFlags);
				db.Migrate().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
				return db;
			});
			services.AddSingleton<SharpMUSH.Library.Plugins.Storage.IArangoStorageAccessor>(sp =>
				(SharpMUSH.Library.Plugins.Storage.IArangoStorageAccessor)sp.GetRequiredService<ISharpDatabase>());
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
		services.AddSingleton<IExpandedObjectDataService, ExpandedObjectDataService>();
		services.AddSingleton<IAttributeService, AttributeService>();
		services.AddSingleton<IManipulateSharpObjectService, ManipulateSharpObjectService>();
		services.AddSingleton<ITaskScheduler, TaskScheduler>();
		services.AddSingleton<IConnectionService, ConnectionService>();
		services.AddSingleton<IOttStore, InMemoryOttStore>();
		services.AddSingleton<HubConnectionRegistry>();
		services.AddSingleton<IAccountSessionStore, DatabaseAccountSessionStore>();
		services.AddSingleton<IAccountService, AccountService>();
		// Unconditional (not gated on JWT config) — AuthController's account-login/register and
		// AdminAccountsController's Wizard gate need it even when JWT auth isn't configured.
		services.AddSingleton<AccountClaimsService>();
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

// Wiki subsystem — backed by whichever ISharpDatabase is active (all three DB backends implement IWikiService).
		services.AddSingleton<WikiMarkdigPipeline>();
		services.AddSingleton<IWikiService>(sp => (IWikiService)sp.GetRequiredService<ISharpDatabase>());

// Package registry — backed by whichever ISharpDatabase is active (all three DB backends implement IPackageRegistryService).
		services.AddSingleton<IPackageRegistryService>(sp => (IPackageRegistryService)sp.GetRequiredService<ISharpDatabase>());

// Dynamic Application registry (Area 21) — same pattern; every DB backend implements IApplicationRegistryService.
// Wrapped in a read-only overlay decorator so the PluginCatalog's IApplicationSource contributions are unioned
// into reads while their plugins are loaded (DB/built-in wins on a slug collision; plugin apps are not
// persisted and not admin-editable). The DB-backed impl is the decorator's inner.
		services.AddSingleton<IApplicationRegistryService>(sp =>
			new Implementation.Services.PluginApplicationRegistryDecorator(
				(IApplicationRegistryService)sp.GetRequiredService<ISharpDatabase>(),
				sp.GetRequiredService<Implementation.Services.PluginCatalog>(),
				sp.GetRequiredService<ILogger<Implementation.Services.PluginApplicationRegistryDecorator>>()));
// Admin-customized layout registry — same cast pattern; every DB backend implements ILayoutRegistryService.
		services.AddSingleton<ILayoutRegistryService>(sp => (ILayoutRegistryService)sp.GetRequiredService<ISharpDatabase>());
// Portal RBAC role registry — same cast pattern; every DB backend implements IRoleRegistryService.
		services.AddSingleton<IRoleRegistryService>(sp => (IRoleRegistryService)sp.GetRequiredService<ISharpDatabase>());
		services.AddSingleton<IPermissionResolver, PermissionResolver>();
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
		services.Configure<CacheInvalidationOptions>(_ => { }); // production defaults (InvalidateAfterHandler = false)
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(QueryCachingBehavior<,>));
		services.AddSingleton(typeof(IStreamPipelineBehavior<,>), typeof(StreamQueryCachingBehavior<,>));
		services.AddSingleton(new ArangoHandle("CurrentSharpMUSHWorld"));
		services.AddSingleton<IMUSHCodeParser, MUSHCodeParser>();
		// Shared MUSH code intelligence — the single source of truth behind both the
		// Language Server (for editors) and the in-server MCP tools (for agents/tooling).
		services.AddSingleton<IMushCodeAnalyzer, MushCodeAnalyzer>();
		services.AddSingleton<IValidateService, ValidateService>();
		services.AddKeyedSingleton(nameof(colorFile), colorFile);
		services.AddOptions<SharpMUSHOptions>().ValidateOnStart();
		services.AddScoped<IValidateOptions<SharpMUSHOptions>, Configuration.Generated.ValidateSharpMUSHOptions>();
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

		if (databaseProvider == DatabaseProvider.ArangoDB && arangoConfig is not null)
		{
			services.AddArango((_, arango) =>
			{
				arango.ConnectionString = arangoConfig.ConnectionString;
				arango.HttpClient = arangoConfig.HttpClient;
				arango.Serializer = arangoConfig.Serializer;
			});
		}

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
				x.AddConsumer<Consumers.TelnetInputConsumer>();
				x.AddConsumer<Consumers.WebSocketInputConsumer>();
				x.AddConsumer<Consumers.GMCPSignalConsumer>();
				x.AddConsumer<Consumers.MSDPUpdateConsumer>();
				x.AddConsumer<Consumers.NAWSUpdateConsumer>();
				x.AddConsumer<Consumers.ConnectionEstablishedConsumer>();
				x.AddConsumer<Consumers.ConnectionClosedConsumer>();
				x.AddConsumer<Consumers.PuebloNegotiatedConsumer>();
				x.AddConsumer<Consumers.MxpNegotiatedConsumer>();
			});

		services.AddFusionCache().TryWithAutoSetup();

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