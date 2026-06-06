using Asp.Versioning;
using Core.Arango;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
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
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.NATS;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
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

// This method gets called by the runtime. Use this method to add services to the container.
// For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
	public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
	{
		services.Configure<BootstrapOptions>(configuration.GetSection(BootstrapOptions.Section));
		services.PostConfigure<BootstrapOptions>(options =>
		{
			var adminUsername = Environment.GetEnvironmentVariable("SHARPMUSH_BOOTSTRAP_USERNAME");
			if (!string.IsNullOrWhiteSpace(adminUsername))
				options.AdminUsername = adminUsername;

			var adminPassword = Environment.GetEnvironmentVariable("SHARPMUSH_BOOTSTRAP_PASSWORD");
			if (!string.IsNullOrWhiteSpace(adminPassword))
				options.AdminPassword = adminPassword;
		});

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
				var db = new MemgraphDatabase(dbLogger, neo4JDriver, password);
				db.Migrate().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
				return db;
			});
		}
		else if (databaseProvider == DatabaseProvider.SurrealDB)
		{
			services.AddSurreal("Endpoint=mem://;Namespace=sharpmush;Database=world")
				.AddInMemoryProvider();
			services.AddSingleton<ISharpDatabase, SurrealDatabase>(x =>
			{
				var dbLogger = x.GetRequiredService<ILogger<SurrealDatabase>>();
				var surrealClient = x.GetRequiredService<ISurrealDbClient>();
				surrealClient.Connect().ConfigureAwait(false).GetAwaiter().GetResult();
				var password = x.GetRequiredService<IPasswordService>();
				var db = new SurrealDatabase(dbLogger, surrealClient, password);
				db.Migrate().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
				return db;
			});
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
				var db = new ArangoDatabase(dbLogger, context, handle, mediator, password);
				db.Migrate().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
				return db;
			});
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

// Add NATS-backed connection state store
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
		services.AddSingleton<IAccountSessionStore, InMemoryAccountSessionStore>();
		services.AddSingleton<IAccountService, AccountService>();
		services.AddHostedService<BootstrapService>();
		services.AddSingleton<ISqlService, SqlService>();
		services.AddSingleton<ICommunicationService, CommunicationService>();
		services.AddSingleton<ILockService, LockService>();
		services.AddSingleton<IGameBroadcastService, GameBroadcastService>();
		services.AddSingleton<IBooleanExpressionParser, BooleanExpressionParser>();
		services.AddSingleton<ICommandDiscoveryService, CommandDiscoveryService>();
		services.AddSingleton<ISortService, SortService>();
		services.AddSingleton<IHookService, HookService>();
		services.AddSingleton<IEventService, EventService>();
		services.AddSingleton<IWarningService, WarningService>();
		services.AddSingleton<IChannelBufferService, InMemoryChannelBufferService>();
		services.AddSingleton<IListenPatternMatcher, ListenPatternMatcher>();
		services.AddSingleton<IListenerRoutingService, ListenerRoutingService>();
		services.AddSingleton<PennMUSHDatabaseParser>();
		services.AddSingleton<IPennMUSHDatabaseConverter, PennMUSHDatabaseConverter>();

// Wiki subsystem — InMemoryWikiService for dev/test; swap for ArangoWikiService when backend is ready.
		services.AddSingleton<WikiMarkdigPipeline>();
		services.AddSingleton<IWikiService, InMemoryWikiService>();

// Scene subsystem — InMemorySceneService for dev/test; swap for a persistent implementation later.
		services.AddSingleton<ISceneService, InMemorySceneService>();

// Pre-render cache for bot-facing static HTML (backed by the shared IMemoryCache from FusionCache setup).
		services.AddMemoryCache();
		services.AddSingleton<Server.Services.IPrerenderCacheService, Server.Services.PrerenderCacheService>();

// Initialize TextFileService
		services.AddSingleton<ITextFileService, Implementation.Services.TextFileService>();
		services.AddSingleton<ILocalizedTextFileService, Implementation.Services.LocalizedTextFileService>();

		services.AddSingleton<ILibraryProvider<FunctionDefinition>, Functions>();
		services.AddSingleton<ILibraryProvider<CommandDefinition>, Commands>();
		services.AddSingleton(x => x.GetService<ILibraryProvider<FunctionDefinition>>()!.Get());
		services.AddSingleton(x => x.GetService<ILibraryProvider<CommandDefinition>>()!.Get());

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

// Read Serilog configuration from appsettings.json (MinimumLevel, Overrides, WriteTo, Enrich)
			var loggerConfig = new LoggerConfiguration()
				.ReadFrom.Configuration(configuration);

			logging.AddSerilog(loggerConfig.CreateLogger());
		});

// Configure NATS messaging for message queue integration
		services.AddNatsMainProcessMessaging(
			options => { options.Url = natsUrl; },
			x =>
			{
// Register consumers for input messages from ConnectionServer
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
		// Authentication setup — three cases:
		// 1) Dev + no JWT key: DebugAuth only (original behaviour)
		// 2) Dev + JWT key: DebugAuth default, JWT bearer as additional scheme
		// 3) Prod + JWT key: JWT bearer only
		var jwtSection = configuration.GetSection(JwtOptions.Section);
		var jwtKey = jwtSection["SigningKey"];

		if (!string.IsNullOrWhiteSpace(jwtKey))
		{
			services.Configure<JwtOptions>(jwtSection);
			services.AddSingleton<IJwtService, JwtService>();

			// In dev: DebugAuth remains the default scheme so existing dev tooling still works.
			// JWT bearer is registered as an additional scheme for portal endpoints.
			// In prod: JWT bearer is the sole default scheme.
			var authBuilder = environment.IsDevelopment()
				? services.AddAuthentication(DebugAuthenticationHandler.SchemeName)
					.AddScheme<AuthenticationSchemeOptions, DebugAuthenticationHandler>(
						DebugAuthenticationHandler.SchemeName, _ => { })
				: services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

			authBuilder.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
			{
				opts.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
					ValidateIssuer = !string.IsNullOrWhiteSpace(jwtSection["Issuer"]),
					ValidIssuer = jwtSection["Issuer"],
					ValidateAudience = !string.IsNullOrWhiteSpace(jwtSection["Audience"]),
					ValidAudience = jwtSection["Audience"],
					ValidateLifetime = true,
					ClockSkew = TimeSpan.FromSeconds(30),
					// W-3: Explicitly map sub→NameIdentifier and role→ClaimTypes.Role instead of
					// relying on JwtSecurityTokenHandler.DefaultInboundClaimTypeMap silently doing it.
					NameClaimType = JwtRegisteredClaimNames.Sub,
					RoleClaimType = ClaimTypes.Role,
				};

				// W-5: Warn when issuer/audience validation is disabled (common misconfiguration).
				var validateIssuer   = !string.IsNullOrWhiteSpace(jwtSection["Issuer"]);
				var validateAudience = !string.IsNullOrWhiteSpace(jwtSection["Audience"]);
				if (!validateIssuer || !validateAudience)
				{
					using var loggerFactory = LoggerFactory.Create(b => b.AddConsole());
					var startupLogger = loggerFactory.CreateLogger<Startup>();
					startupLogger.LogWarning(
						"W-5: JWT {Missing} validation is DISABLED — set Jwt:{Missing} in config for production.",
						!validateIssuer && !validateAudience ? "Issuer+Audience"
						: !validateIssuer ? "Issuer" : "Audience",
						!validateIssuer && !validateAudience ? "Issuer and Jwt:Audience"
						: !validateIssuer ? "Issuer" : "Audience");
				}
			});
		}
		else if (environment.IsDevelopment())
		{
			// No JWT configured: dev-only DebugAuth handler (original behaviour).
			services.AddAuthentication(DebugAuthenticationHandler.SchemeName)
				.AddScheme<AuthenticationSchemeOptions, DebugAuthenticationHandler>(
					DebugAuthenticationHandler.SchemeName, _ => { });
		}

		// JWT infrastructure (role derivation + refresh tokens) is always registered
		// so that the services are available even before a signing key is configured.
		services.AddSingleton<IRoleDerivationService, RoleDerivationService>();
		services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();

		services.AddSignalR();

		// ── API Versioning ────────────────────────────────────────────────────
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

		// ── Rate Limiting ─────────────────────────────────────────────────────
		// Named "public-api" policy: fixed window, 30 req/min per client IP,
		// queue depth 5.  Auth endpoints opt in via [EnableRateLimiting("public-api")].
		services.AddRateLimiter(opts =>
		{
			opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			opts.AddFixedWindowLimiter("public-api", limiterOpts =>
			{
				limiterOpts.PermitLimit = 30;
				limiterOpts.Window = TimeSpan.FromMinutes(1);
				limiterOpts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
				limiterOpts.QueueLimit = 5;
			});
		});

		// ── RFC 7807 Problem Details ──────────────────────────────────────────
		services.AddProblemDetails();
		services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

		services.AddAuthorization();
		services.AddRazorPages();
		services.AddControllers();
		services.AddQuartzHostedService();
		services.AddHostedService<StartupHandler>();
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

// Add container resource detection for Kubernetes environments
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